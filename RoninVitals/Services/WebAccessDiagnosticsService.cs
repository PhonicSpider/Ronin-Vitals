using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;
using static RoninVitals.Services.WebAccessRules;

namespace RoninVitals.Services;

/// <summary>
/// The checks behind "a website or game won't load". They answer "is it me or them?" by comparing (your DNS against a
/// public one, popular sites against each other) and by ruling out local causes that quietly break only some sites (hosts
/// file, firewall rule, wrong clock, stale browser cache). Checks are read-only. The two fixes are small: re-sync the
/// clock and clear browser caches. How results are interpreted lives in <see cref="WebAccessRules"/>.
/// </summary>
public static class WebAccessDiagnosticsService
{
    private const int HostsFileLongListThreshold = 200;
    private const int MaxListedEntries = 25;
    private const int SlowDnsMilliseconds = 400;
    private const int SlowDnsMultiple = 4;
    private const long BloatedCacheBytes = 2L << 30;
    private const string CaptivePortalMessage =
        "This network is asking you to sign in first (a sign-in page, like in a hotel, cafe or campus). Until you do, no site will load. Open a web browser and look for the sign-in page, or try visiting http://neverssl.com.";

    private static readonly string[] DnsTestHostNames = ["www.microsoft.com", "www.wikipedia.org", "store.steampowered.com"];
    private static readonly string[] ClockReferenceUrls = ["https://www.microsoft.com", "https://www.google.com", "https://www.cloudflare.com"];
    private static readonly TimeSpan FirewallQueryTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ClockSyncTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SiteTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ClockProbeTimeout = TimeSpan.FromSeconds(6);

    private sealed record DnsComparisonRow(string HostName, DnsLookup System, DnsLookup Public);

    // ── DNS: does your PC's DNS agree with a public one? ────────────────────────────────────

    public static CheckResult CheckDnsVersusPublic()
    {
        var techCommand = $"Resolve-DnsName <site>; Resolve-DnsName <site> -Server {DnsProbe.PublicResolver}   (for {string.Join(", ", DnsTestHostNames)})";
        try
        {
            var rows = DnsTestHostNames
                .Select(hostName => new DnsComparisonRow(hostName, DnsProbe.ResolveWithSystemDns(hostName), DnsProbe.ResolveWithPublicDns(hostName)))
                .ToList();

            var configuredServers = ActiveAdapter.Find()?.GetIPProperties().DnsAddresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .ToList() ?? [];
            var techDetail = $"DNS servers on the active adapter: {(configuredServers.Count == 0 ? "(none found)" : string.Join(", ", configuredServers))}. " +
                string.Join(" | ", rows.Select(DescribeRow));

            var total = rows.Count;
            var systemResolved = rows.Count(row => row.System.Succeeded);
            var publicResolved = rows.Count(row => row.Public.Succeeded);
            var failedHostNames = string.Join(", ", rows.Where(row => !row.System.Succeeded).Select(row => row.HostName));

            if (systemResolved == 0 && publicResolved == 0)
                return new CheckResult(CheckStatus.Bad, "Neither your PC's DNS nor a public DNS could look up any of the test sites, so name lookups aren't working at all right now.", techCommand, techDetail);

            if (systemResolved < total && publicResolved > systemResolved)
                return new CheckResult(CheckStatus.Bad,
                    $"Your PC's DNS server failed to look up {total - systemResolved} of {total} test sites ({failedHostNames}) that a public DNS found instantly. The DNS server your PC is using is the problem.",
                    techCommand, techDetail);

            if (systemResolved < total)
                return new CheckResult(CheckStatus.Warn, $"Your PC's DNS failed to look up {total - systemResolved} of {total} test sites ({failedHostNames}).", techCommand, techDetail);

            if (publicResolved < total)
                return new CheckResult(CheckStatus.Pass,
                    $"Your PC's DNS looked up all {total} test sites correctly. A public DNS couldn't be reached for comparison, which some networks block on purpose and is fine.",
                    techCommand, techDetail);

            var systemAverage = rows.Average(row => row.System.Milliseconds);
            var publicAverage = rows.Average(row => row.Public.Milliseconds);
            return systemAverage > SlowDnsMilliseconds && systemAverage > publicAverage * SlowDnsMultiple
                ? new CheckResult(CheckStatus.Warn, $"Your PC's DNS works but is slow ({systemAverage:0} ms on average, against {publicAverage:0} ms for a public DNS), so pages will feel slow to start loading.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Pass, $"Your PC's DNS looked up all {total} test sites correctly, the same as a public DNS.", techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish comparing DNS servers.", techCommand, DescribeException(exception));
        }
    }

    private static string DescribeRow(DnsComparisonRow row) =>
        $"{row.HostName}: yours {(row.System.Succeeded ? "ok" : "FAILED")} ({row.System.Milliseconds} ms, {row.System.Detail}), " +
        $"public {(row.Public.Succeeded ? "ok" : "failed")} ({row.Public.Milliseconds} ms, {row.Public.Detail})";

    // ── Hosts file ──────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckHostsFile()
    {
        const string techCommand = @"Get-Content $env:SystemRoot\System32\drivers\etc\hosts | Where-Object { $_ -notmatch '^\s*#' -and $_.Trim() }";
        var hostsPath = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        try
        {
            var entries = ParseHostsFile(File.ReadAllText(hostsPath));
            if (entries.Count == 0)
                return new CheckResult(CheckStatus.Pass, "Your hosts file has no custom entries, so nothing on this PC is quietly blocking or redirecting websites.", techCommand, $"{hostsPath}: no active entries.");

            var blockCount = entries.Count(entry => entry.IsBlock);
            var description = entries.Count > HostsFileLongListThreshold
                ? "a long blocklist (typical of an ad or tracker blocker)"
                : $"{entries.Count} {Plural(entries.Count, "entry", "entries")}";
            var techDetail = $"{hostsPath}: {entries.Count} active entr{(entries.Count == 1 ? "y" : "ies")} ({blockCount} point to 0.0.0.0/127.0.0.1 = blocked, {entries.Count - blockCount} redirect elsewhere). First {MaxListedEntries}: " +
                             string.Join(" | ", entries.Take(MaxListedEntries).Select(entry => $"{entry.Address} {entry.Host}"));

            return new CheckResult(CheckStatus.Warn,
                $"Your hosts file has {description} that block or redirect website names (for example {Examples(entries.Select(entry => entry.Host))}). If one of them is the site or game that won't load, that is the reason.",
                techCommand, techDetail);
        }
        catch (FileNotFoundException)
        {
            return new CheckResult(CheckStatus.Pass, "There is no hosts file, so nothing is being blocked or redirected that way.", techCommand, $"{hostsPath} not found.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read your hosts file.", techCommand, DescribeException(exception));
        }
    }

    // ── Windows Firewall ────────────────────────────────────────────────────────────────────

    public static CheckResult CheckFirewallBlocking()
    {
        const string techCommand = "Get-NetFirewallProfile -PolicyStore ActiveStore; Get-NetFirewallRule -Direction Outbound -Action Block -Enabled True -PolicyStore ActiveStore";
        // -ErrorAction SilentlyContinue on the rules query: with no matching rules (the healthy case) the cmdlet raises an
        // error, which would otherwise turn "nothing is blocked" into a failed check.
        const string script =
            "$profiles=@(Get-NetFirewallProfile -PolicyStore ActiveStore | Where-Object { [string]$_.Enabled -eq 'True' } | ForEach-Object { [string]$_.Name }); " +
            "$blocks=@(Get-NetFirewallRule -Direction Outbound -Action Block -Enabled True -PolicyStore ActiveStore -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.DisplayName }); " +
            "[pscustomobject]@{ Profiles=$profiles; Blocks=$blocks } | ConvertTo-Json -Compress";

        var query = PowerShellRunner.Run(script, FirewallQueryTimeout);
        if (!query.Succeeded || query.Output.Length == 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Firewall rules.", techCommand,
                query.TimedOut ? "Timed out reading firewall rules." : $"Get-NetFirewallRule failed: {query.Error.Split('\n', 2)[0].Trim()}");

        List<string> enabledProfiles, blockRules;
        try
        {
            using var document = JsonDocument.Parse(query.Output);
            enabledProfiles = ReadStrings(document.RootElement, "Profiles");
            blockRules = ReadStrings(document.RootElement, "Blocks");
        }
        catch (JsonException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Firewall rules.", techCommand, DescribeException(exception));
        }

        var otherFirewalls = FindOtherFirewalls();
        var otherFirewallNote = otherFirewalls.Count > 0
            ? $" Another firewall is also active on this PC ({string.Join(" and ", otherFirewalls)}), and it can block sites too, so check its settings if something is still blocked."
            : string.Empty;
        var techDetail = $"Firewall on for: {(enabledProfiles.Count == 0 ? "no profile" : string.Join(", ", enabledProfiles))}. Outgoing block rules: {blockRules.Count}" +
                         (blockRules.Count > 0 ? " - " + string.Join(" | ", blockRules.Take(MaxListedEntries)) : "") +
                         (otherFirewalls.Count > 0 ? $". Other firewalls registered: {string.Join(", ", otherFirewalls)}." : ".");

        if (enabledProfiles.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows Firewall is switched off, so it isn't blocking anything." + otherFirewallNote, techCommand, techDetail);

        if (blockRules.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows Firewall isn't blocking any outgoing connections (by default it only guards incoming ones)." + otherFirewallNote, techCommand, techDetail);

        return new CheckResult(CheckStatus.Warn,
            $"Windows Firewall has {blockRules.Count} {Plural(blockRules.Count, "rule", "rules")} that block outgoing connections (for example {Examples(blockRules)}). If one of them covers the program or game that won't connect, that is why." + otherFirewallNote,
            techCommand, techDetail);
    }

    /// <summary>Firewalls other than Windows' own that Security Center knows about (an optional extra; not every PC has the class).</summary>
    private static List<string> FindOtherFirewalls()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM FirewallProduct");
            foreach (ManagementObject product in searcher.Get())
            {
                if (product["displayName"] is string name && !name.Contains("Windows", StringComparison.OrdinalIgnoreCase) && !names.Contains(name))
                    names.Add(name);
            }
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or InvalidCastException) { /* optional */ }
        return names;
    }

    /// <summary>PowerShell's JSON gives a bare string for one value and an array for several.</summary>
    private static List<string> ReadStrings(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value)) return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "").ToList(),
            JsonValueKind.String => [value.GetString() ?? ""],
            _ => [],
        };
    }

    // ── Popular sites: is it me or them? ────────────────────────────────────────────────────

    public static CheckResult CheckPopularSites()
    {
        var techCommand = $"foreach ($url in 'https://www.google.com','https://store.steampowered.com','https://store.epicgames.com', ...) {{ try {{ (Invoke-WebRequest $url -Method Head -UseBasicParsing -TimeoutSec {SiteTimeout.TotalSeconds:0}).StatusCode }} catch {{ $_.Exception.Message }} }}";
        try
        {
            var captivePortal = SiteProbe.DetectCaptivePortalAsync().GetAwaiter().GetResult();
            if (captivePortal.Detected)
                return new CheckResult(CheckStatus.Bad, CaptivePortalMessage, techCommand, captivePortal.Detail);

            using var client = SiteProbe.CreateClient(followRedirects: true, SiteTimeout);
            var siteResults = Task.WhenAll(SiteProbe.PopularSites.Select(site => SiteProbe.ProbeAsync(client, site))).GetAwaiter().GetResult().ToList();
            var summary = SummarizeSites(siteResults);

            var techDetail = captivePortal.Detail + " " + string.Join(" | ", siteResults.Select(result =>
                $"{result.Name}: {(result.Outcome == SiteOutcome.Loaded ? "loaded" : result.Outcome.ToString())} ({result.Detail}, {result.Milliseconds} ms)"));
            return new CheckResult(summary.Status, summary.Text, techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish testing popular sites.", techCommand, DescribeException(exception));
        }
    }

    // ── System clock ────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckSystemClock()
    {
        const string techCommand = "(Invoke-WebRequest https://www.microsoft.com -Method Head -UseBasicParsing).Headers.Date   vs   (Get-Date).ToUniversalTime()";
        var failures = new List<string>();
        using var client = SiteProbe.CreateClient(followRedirects: true, ClockProbeTimeout);

        foreach (var url in ClockReferenceUrls)
        {
            try
            {
                var requestSentAt = DateTimeOffset.UtcNow;
                using var response = client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                var responseReceivedAt = DateTimeOffset.UtcNow;

                if (response.Headers.Date is not { } serverTime)
                {
                    failures.Add($"{url}: no Date header");
                    continue;
                }

                // Compare with the middle of the request so network delay doesn't read as clock error.
                var pcTime = requestSentAt + (responseReceivedAt - requestSentAt) / 2;
                var serverAheadBy = serverTime - pcTime;
                var pcOffset = DescribeSkew(-serverAheadBy);
                var techDetail = $"{url}: server time {serverTime.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z, this PC {requestSentAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z, difference {serverAheadBy.TotalSeconds:+0.0;-0.0} s (this PC is {pcOffset}). The reading is only good to about a second.";

                return ClassifyClockSkew(serverAheadBy) switch
                {
                    ClockState.Fine => new CheckResult(CheckStatus.Pass, "Your PC's clock matches an internet time source, so secure sites won't reject you over the time.", techCommand, techDetail),
                    ClockState.Drifting => new CheckResult(CheckStatus.Warn, $"Your PC's clock is about {pcOffset}. That's enough for some secure sites and game services to start refusing connections.", techCommand, techDetail),
                    _ => new CheckResult(CheckStatus.Bad, $"Your PC's clock is {pcOffset}. A clock that far off makes secure (HTTPS) sites show certificate errors or refuse to load at all.", techCommand, techDetail),
                };
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                failures.Add($"{url}: {exception.GetType().Name}");
            }
        }
        return new CheckResult(CheckStatus.Warn, "Couldn't check your clock against an internet time source.", techCommand, string.Join(" | ", failures));
    }

    /// <summary>SafeAutoFix: asks Windows to re-sync with its time server (<c>w32tm /resync</c>). It only corrects the clock.</summary>
    public static Task<bool> SyncClockAsync() =>
        Task.Run(() => ProcessRunner.Run("w32tm", "/resync /force", ClockSyncTimeout).Succeeded);

    // ── Browser caches ──────────────────────────────────────────────────────────────────────

    public static CheckResult CheckBrowserCache()
    {
        const string techCommand = @"Get-ChildItem '$env:LOCALAPPDATA\Microsoft\Edge\User Data\*\Cache', '$env:LOCALAPPDATA\Google\Chrome\User Data\*\Cache', '$env:LOCALAPPDATA\Mozilla\Firefox\Profiles\*\cache2' -Recurse -File | Measure-Object Length -Sum";
        try
        {
            var cacheFolders = BrowserCaches.Find();
            if (cacheFolders.Count == 0)
                return new CheckResult(CheckStatus.Pass, "No browser cache folders were found for Edge, Chrome, Firefox, Brave or Opera.", techCommand, "None of the usual cache folders exist.");

            var sizeByBrowser = cacheFolders
                .GroupBy(folder => folder.Browser)
                .Select(group => (Browser: group.Key, Bytes: group.Sum(folder => BrowserCaches.SizeInBytes(folder.Path))))
                .OrderByDescending(browser => browser.Bytes)
                .ToList();
            var totalBytes = sizeByBrowser.Sum(browser => browser.Bytes);
            var breakdown = string.Join(", ", sizeByBrowser.Where(browser => browser.Bytes > 0).Select(browser => $"{browser.Browser} {BrowserCaches.FormatSize(browser.Bytes)}"));
            var techDetail = $"Total {BrowserCaches.FormatSize(totalBytes)} across {cacheFolders.Count} cache folder(s): " + string.Join(" | ", cacheFolders.Select(folder => folder.Path));

            return totalBytes >= BloatedCacheBytes
                ? new CheckResult(CheckStatus.Warn, $"Your browsers are holding {BrowserCaches.FormatSize(totalBytes)} of saved web files ({breakdown}). A bloated cache can make sites load wrong or not at all, and clearing it is safe.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Pass,
                    $"Your browsers' saved web files are a normal size ({BrowserCaches.FormatSize(totalBytes)}{(breakdown.Length > 0 ? ": " + breakdown : "")}). If just one site won't load, try it in a private or incognito window: if it works there, that browser's saved data for the site is the cause.",
                    techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check your browsers' cache sizes.", techCommand, DescribeException(exception));
        }
    }

    /// <summary>
    /// SafeAutoFix: empties browser caches. Refuses while any of those browsers is open, since deleting files from a cache
    /// in use can corrupt it.
    /// </summary>
    public static Task<bool> ClearBrowserCachesAsync() => Task.Run(() =>
    {
        var cacheFolders = BrowserCaches.Find();
        var openBrowsers = BrowserCaches.FindRunningBrowsers(cacheFolders);
        if (openBrowsers.Count > 0)
            throw new FixDeclinedException($"Close {string.Join(" and ", openBrowsers)} first, then press this again. Clearing a browser's cache while it's open can corrupt it.");

        foreach (var cacheFolder in cacheFolders) BrowserCaches.ClearContents(cacheFolder.Path);
        return true;
    });
}
