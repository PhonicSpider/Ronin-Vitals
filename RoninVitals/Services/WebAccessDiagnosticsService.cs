using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using RoninVitals.Models;
using static RoninVitals.Services.WebAccessRules;

namespace RoninVitals.Services;

/// <summary>
/// The checks behind "a website or game won't load". The central question is "is it me or them?", so most of
/// these either compare (your DNS vs a public one, popular sites against each other) or rule out a local cause
/// that silently breaks just some sites (a hosts-file entry, a firewall rule, a wrong clock, a stale browser
/// cache). All checks are read-only. The two fixes are small and safe: re-sync the clock, and clear browser caches
/// (never passwords, cookies or history). The logic that decides what results mean lives in
/// <see cref="WebAccessRules"/> so it can be unit-tested exactly.
/// </summary>
public static class WebAccessDiagnosticsService
{
    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    private static string Detail(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    // ── 1. Does your DNS agree with a public one? ───────────────────────────────────────────────

    private static readonly string[] DnsProbeNames = { "www.microsoft.com", "www.wikipedia.org", "store.steampowered.com" };
    private const string PublicResolver = "1.1.1.1";

    private static (bool Ok, long Ms, string Detail) ResolveWithSystemDns(string name)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var task = Dns.GetHostAddressesAsync(name);
            if (!task.Wait(TimeSpan.FromSeconds(5))) return (false, clock.ElapsedMilliseconds, "timed out");
            var addresses = task.Result;
            return addresses.Length > 0
                ? (true, clock.ElapsedMilliseconds, string.Join("/", addresses.Take(2)))
                : (false, clock.ElapsedMilliseconds, "no address returned");
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException se)
        {
            return (false, clock.ElapsedMilliseconds, se.SocketErrorCode.ToString());
        }
        catch (Exception ex)
        {
            return (false, clock.ElapsedMilliseconds, ex.GetType().Name);
        }
    }

    private static (bool Ok, long Ms, string Detail) ResolveWithPublicDns(string name)
    {
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var clock = Stopwatch.StartNew();
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 3000;
            udp.Client.SendTimeout = 3000;
            udp.Connect(PublicResolver, 53);
            var query = BuildDnsQuery(name, id);
            udp.Send(query, query.Length);
            IPEndPoint? remote = null;
            var reply = ParseDnsReply(udp.Receive(ref remote), id);
            return (reply.Resolved, clock.ElapsedMilliseconds,
                reply.Valid ? $"{DescribeResponseCode(reply.ResponseCode)}, {reply.AnswerCount} answer(s)" : "unrecognized reply");
        }
        catch (SocketException ex)
        {
            return (false, clock.ElapsedMilliseconds,
                ex.SocketErrorCode == SocketError.TimedOut ? "no reply (UDP port 53 may be blocked)" : ex.SocketErrorCode.ToString());
        }
    }

    public static CheckResult CheckDnsVersusPublic()
    {
        var techCommand = $"Resolve-DnsName <site>; Resolve-DnsName <site> -Server {PublicResolver}   (for {string.Join(", ", DnsProbeNames)})";
        try
        {
            var system = DnsProbeNames.Select(n => (Name: n, R: ResolveWithSystemDns(n))).ToList();
            var pub = DnsProbeNames.Select(n => (Name: n, R: ResolveWithPublicDns(n))).ToList();

            var configured = ActiveAdapter.Find()?.GetIPProperties().DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList() ?? new List<string>();
            var techDetail = $"DNS servers on the active adapter: {(configured.Count == 0 ? "(none found)" : string.Join(", ", configured))}. " +
                string.Join(" | ", system.Zip(pub, (s, p) => $"{s.Name}: yours {(s.R.Ok ? "ok" : "FAILED")} ({s.R.Ms} ms, {s.R.Detail}), public {(p.R.Ok ? "ok" : "failed")} ({p.R.Ms} ms, {p.R.Detail})"));

            var n = DnsProbeNames.Length;
            var systemOk = system.Count(s => s.R.Ok);
            var publicOk = pub.Count(p => p.R.Ok);
            var failedNames = string.Join(", ", system.Where(s => !s.R.Ok).Select(s => s.Name));

            if (systemOk == 0 && publicOk == 0)
                return new CheckResult(CheckStatus.Bad, "Neither your PC's DNS nor a public DNS could look up any of the test sites, so name lookups aren't working at all right now.", techCommand, techDetail);

            if (systemOk < n && publicOk > systemOk)
                return new CheckResult(CheckStatus.Bad,
                    $"Your PC's DNS server failed to look up {n - systemOk} of {n} test sites ({failedNames}) that a public DNS found instantly. The DNS server your PC is using is the problem.",
                    techCommand, techDetail);

            if (systemOk < n)
                return new CheckResult(CheckStatus.Warn, $"Your PC's DNS failed to look up {n - systemOk} of {n} test sites ({failedNames}).", techCommand, techDetail);

            // Everything resolved on this PC.
            var ownAverage = system.Average(s => s.R.Ms);
            if (publicOk == n)
            {
                var publicAverage = pub.Average(p => p.R.Ms);
                return ownAverage > 400 && ownAverage > publicAverage * 4
                    ? new CheckResult(CheckStatus.Warn, $"Your PC's DNS works but is slow ({ownAverage:0} ms on average, against {publicAverage:0} ms for a public DNS), so pages will feel slow to start loading.", techCommand, techDetail)
                    : new CheckResult(CheckStatus.Pass, $"Your PC's DNS looked up all {n} test sites correctly, the same as a public DNS.", techCommand, techDetail);
            }

            return new CheckResult(CheckStatus.Pass,
                $"Your PC's DNS looked up all {n} test sites correctly. A public DNS couldn't be reached for comparison, which some networks block on purpose and is fine.",
                techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish comparing DNS servers.", techCommand, Detail(ex));
        }
    }

    // ── 2. Is the hosts file blocking or redirecting sites? ─────────────────────────────────────

    public static CheckResult CheckHostsFile()
    {
        const string techCommand = @"Get-Content $env:SystemRoot\System32\drivers\etc\hosts | Where-Object { $_ -notmatch '^\s*#' -and $_.Trim() }";
        var path = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        try
        {
            var entries = ParseHostsFile(File.ReadAllText(path));
            if (entries.Count == 0)
                return new CheckResult(CheckStatus.Pass, "Your hosts file has no custom entries, so nothing on this PC is quietly blocking or redirecting websites.", techCommand, $"{path}: no active entries.");

            var blocks = entries.Count(e => e.IsBlock);
            var hosts = entries.Select(e => e.Host).Distinct().ToList();
            var examples = string.Join(", ", hosts.Take(3)) + (hosts.Count > 3 ? $" and {hosts.Count - 3} more" : "");
            var kind = entries.Count > 200 ? "a long blocklist (typical of an ad or tracker blocker)" : $"{entries.Count} {Plural(entries.Count, "entry", "entries")}";
            var techDetail = $"{path}: {entries.Count} active entr{(entries.Count == 1 ? "y" : "ies")} ({blocks} point to 0.0.0.0/127.0.0.1 = blocked, {entries.Count - blocks} redirect elsewhere). First 25: " +
                             string.Join(" | ", entries.Take(25).Select(e => $"{e.Address} {e.Host}"));

            return new CheckResult(CheckStatus.Warn,
                $"Your hosts file has {kind} that block or redirect website names (for example {examples}). If one of them is the site or game that won't load, that is the reason.",
                techCommand, techDetail);
        }
        catch (FileNotFoundException)
        {
            return new CheckResult(CheckStatus.Pass, "There is no hosts file, so nothing is being blocked or redirected that way.", techCommand, $"{path} not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read your hosts file.", techCommand, Detail(ex));
        }
    }

    // ── 3. Is the Windows Firewall blocking outgoing connections? ───────────────────────────────

    private static List<string> OtherFirewallProducts()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM FirewallProduct");
            foreach (ManagementObject product in searcher.Get())
                if (product["displayName"] is string name && !name.Contains("Windows", StringComparison.OrdinalIgnoreCase) && !names.Contains(name))
                    names.Add(name);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidCastException) { /* optional extra, not every PC has the class */ }
        return names;
    }

    public static CheckResult CheckFirewallBlocking()
    {
        const string techCommand = "Get-NetFirewallProfile -PolicyStore ActiveStore; Get-NetFirewallRule -Direction Outbound -Action Block -Enabled True -PolicyStore ActiveStore";
        const string script =
            "$p=@(Get-NetFirewallProfile -PolicyStore ActiveStore | Where-Object { [string]$_.Enabled -eq 'True' } | ForEach-Object { [string]$_.Name }); " +
            // SilentlyContinue on purpose: with no matching rules (the normal, healthy case) this cmdlet RAISES an error,
            // which under the global Stop preference would turn "nothing is blocked" into a failed check.
            "$r=@(Get-NetFirewallRule -Direction Outbound -Action Block -Enabled True -PolicyStore ActiveStore -ErrorAction SilentlyContinue | ForEach-Object { [string]$_.DisplayName }); " +
            "[pscustomobject]@{ Profiles=$p; Blocks=$r } | ConvertTo-Json -Compress";

        var result = PowerShellRunner.Run(script, TimeSpan.FromSeconds(90));
        if (result.ExitCode != 0 || result.Output.Length == 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Firewall rules.", techCommand,
                result.TimedOut ? "Timed out reading firewall rules." : $"Get-NetFirewallRule failed: {result.Error.Split('\n', 2)[0].Trim()}");

        List<string> profiles, blocks;
        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            profiles = ReadStrings(doc.RootElement, "Profiles");
            blocks = ReadStrings(doc.RootElement, "Blocks");
        }
        catch (JsonException ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Firewall rules.", techCommand, Detail(ex));
        }

        var others = OtherFirewallProducts();
        var otherNote = others.Count > 0 ? $" Another firewall is also active on this PC ({string.Join(" and ", others)}), and it can block sites too, so check its settings if something is still blocked." : string.Empty;
        var techDetail = $"Firewall on for: {(profiles.Count == 0 ? "no profile" : string.Join(", ", profiles))}. Outgoing block rules: {blocks.Count}" +
                         (blocks.Count > 0 ? " - " + string.Join(" | ", blocks.Take(25)) : "") + (others.Count > 0 ? $". Other firewalls registered: {string.Join(", ", others)}." : ".");

        if (profiles.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows Firewall is switched off, so it isn't blocking anything." + otherNote, techCommand, techDetail);

        if (blocks.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows Firewall isn't blocking any outgoing connections (by default it only guards incoming ones)." + otherNote, techCommand, techDetail);

        var examples = string.Join(", ", blocks.Take(3)) + (blocks.Count > 3 ? $" and {blocks.Count - 3} more" : "");
        return new CheckResult(CheckStatus.Warn,
            $"Windows Firewall has {blocks.Count} {Plural(blocks.Count, "rule", "rules")} that block outgoing connections (for example {examples}). If one of them covers the program or game that won't connect, that is why." + otherNote,
            techCommand, techDetail);
    }

    private static List<string> ReadStrings(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return new List<string>();
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? "").ToList(),
            JsonValueKind.String => new List<string> { value.GetString() ?? "" },
            _ => new List<string>(),
        };
    }

    // ── 4. Do popular sites and game services load? ("is it me or them?") ───────────────────────

    private static readonly (string Name, string Group, string Url)[] PopularSites =
    {
        ("Google", "General", "https://www.google.com"),
        ("Microsoft", "General", "https://www.microsoft.com"),
        ("Cloudflare", "General", "https://www.cloudflare.com"),
        ("Steam", "Gaming", "https://store.steampowered.com"),
        ("Epic Games", "Gaming", "https://store.epicgames.com"),
        ("Xbox", "Gaming", "https://www.xbox.com"),
        ("Battle.net", "Gaming", "https://battle.net"),
        ("Riot Games", "Gaming", "https://www.riotgames.com"),
        ("EA", "Gaming", "https://www.ea.com"),
        ("Discord", "Gaming", "https://discord.com"),
    };

    private static HttpClient NewClient(bool followRedirects, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(6),
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        // Several of these sites refuse a request with no browser-like agent at all.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) RoninVitals/0.1");
        return client;
    }

    private static async Task<SiteResult> ProbeSiteAsync(HttpClient client, (string Name, string Group, string Url) site)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, site.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var code = (int)response.StatusCode;
            // Any real answer means the site is reachable: HEAD is often refused with 403/405, which still proves it loaded.
            return code >= 500
                ? new SiteResult(site.Name, site.Group, SiteOutcome.ServerError, clock.ElapsedMilliseconds, $"HTTP {code}")
                : new SiteResult(site.Name, site.Group, SiteOutcome.Loaded, clock.ElapsedMilliseconds, $"HTTP {code}");
        }
        catch (TaskCanceledException)
        {
            return new SiteResult(site.Name, site.Group, SiteOutcome.TimedOut, clock.ElapsedMilliseconds, "timed out");
        }
        catch (HttpRequestException ex)
        {
            var outcome = ex.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => SiteOutcome.DnsFailed,
                HttpRequestError.SecureConnectionError => SiteOutcome.TlsFailed,
                _ => ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut } ? SiteOutcome.TimedOut : SiteOutcome.OtherFailure,
            };
            var label = outcome switch
            {
                SiteOutcome.DnsFailed => "DNS lookup failed",
                SiteOutcome.TlsFailed => "secure connection (TLS) failed",
                SiteOutcome.TimedOut => "timed out",
                _ => ex.InnerException?.Message ?? ex.Message,
            };
            return new SiteResult(site.Name, site.Group, outcome, clock.ElapsedMilliseconds, label);
        }
    }

    /// <summary>The "sign in to this network" page (a captive portal) makes every site fail until you accept it.
    /// Windows' own connectivity test page is used because it is built for exactly this question.</summary>
    private static string? CaptivePortalProblem(out string detail)
    {
        try
        {
            using var client = NewClient(followRedirects: false, TimeSpan.FromSeconds(5));
            using var response = client.GetAsync("http://www.msftconnecttest.com/connecttest.txt").GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var captive = LooksLikeCaptivePortal((int)response.StatusCode, body);
            detail = $"connecttest.txt -> HTTP {(int)response.StatusCode}, {(captive ? "not the expected page" : "expected page")}.";
            return captive ? "This network is asking you to sign in first (a sign-in page, like in a hotel, cafe or campus). Until you do, no site will load. Open a web browser and look for the sign-in page, or try visiting http://neverssl.com." : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            detail = "connecttest.txt unreachable (" + ex.GetType().Name + ").";
            return null; // no answer tells us nothing; the site test below will show the real picture
        }
    }

    public static CheckResult CheckPopularSites()
    {
        const string techCommand =
            "foreach ($u in 'https://www.google.com','https://store.steampowered.com','https://store.epicgames.com', ...) { try { (Invoke-WebRequest $u -Method Head -UseBasicParsing -TimeoutSec 8).StatusCode } catch { $_.Exception.Message } }";
        try
        {
            var portal = CaptivePortalProblem(out var portalDetail);
            if (portal is not null)
                return new CheckResult(CheckStatus.Bad, portal, techCommand, portalDetail);

            using var client = NewClient(followRedirects: true, TimeSpan.FromSeconds(8));
            var results = Task.WhenAll(PopularSites.Select(s => ProbeSiteAsync(client, s))).GetAwaiter().GetResult().ToList();
            var summary = SummarizeSites(results);

            var techDetail = portalDetail + " " + string.Join(" | ", results.Select(r => $"{r.Name}: {(r.Outcome == SiteOutcome.Loaded ? "loaded" : r.Outcome.ToString())} ({r.Detail}, {r.Milliseconds} ms)"));
            return new CheckResult(summary.Status, summary.Text, techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish testing popular sites.", techCommand, Detail(ex));
        }
    }

    // ── 5. Is the clock right? ──────────────────────────────────────────────────────────────────

    private static readonly string[] ClockReferenceUrls = { "https://www.microsoft.com", "https://www.google.com", "https://www.cloudflare.com" };

    public static CheckResult CheckSystemClock()
    {
        const string techCommand = "(Invoke-WebRequest https://www.microsoft.com -Method Head -UseBasicParsing).Headers.Date   vs   (Get-Date).ToUniversalTime()";
        var attempts = new List<string>();
        using var client = NewClient(followRedirects: true, TimeSpan.FromSeconds(6));
        foreach (var url in ClockReferenceUrls)
        {
            try
            {
                var before = DateTimeOffset.UtcNow;
                using var response = client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                var after = DateTimeOffset.UtcNow;
                if (response.Headers.Date is not { } serverTime)
                {
                    attempts.Add($"{url}: no Date header");
                    continue;
                }

                // Compare against the middle of the request, so network delay doesn't read as clock error.
                var skew = serverTime - (before + (after - before) / 2);
                var techDetail = $"{url}: server time {serverTime.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z, this PC {before.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z, difference {skew.TotalSeconds:+0.0;-0.0} s (this PC is {DescribeSkew(-skew)}). The reading is only good to about a second.";
                return ClassifyClockSkew(skew) switch
                {
                    ClockState.Fine => new CheckResult(CheckStatus.Pass, "Your PC's clock matches an internet time source, so secure sites won't reject you over the time.", techCommand, techDetail),
                    ClockState.Drifting => new CheckResult(CheckStatus.Warn, $"Your PC's clock is about {DescribeSkew(-skew)}. That's enough for some secure sites and game services to start refusing connections.", techCommand, techDetail),
                    _ => new CheckResult(CheckStatus.Bad, $"Your PC's clock is {DescribeSkew(-skew)}. A clock that far off makes secure (HTTPS) sites show certificate errors or refuse to load at all.", techCommand, techDetail),
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                attempts.Add($"{url}: {ex.GetType().Name}");
            }
        }
        return new CheckResult(CheckStatus.Warn, "Couldn't check your clock against an internet time source.", techCommand, string.Join(" | ", attempts));
    }

    /// <summary>SafeAutoFix - asks Windows to re-sync with its time server (<c>w32tm /resync</c>). Only moves the
    /// clock to the correct time; nothing else on the PC changes.</summary>
    public static async Task<bool> SyncClockAsync() => await Task.Run(() =>
    {
        var psi = new ProcessStartInfo { FileName = "w32tm", Arguments = "/resync /force", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi);
        if (process is null) return false;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        return process.WaitForExit(30_000) && process.ExitCode == 0;
    });

    // ── 6. Browser caches ───────────────────────────────────────────────────────────────────────

    private sealed record CacheFolder(string Browser, string ProcessName, string Path);

    private static List<CacheFolder> FindBrowserCaches()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var found = new List<CacheFolder>();

        // Chromium browsers keep one Cache folder per profile under "User Data".
        foreach (var (browser, process, userData) in new[]
        {
            ("Edge", "msedge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Chrome", "chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Brave", "brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
        })
        {
            if (!Directory.Exists(userData)) continue;
            foreach (var profile in SafeDirectories(userData).Where(d => Path.GetFileName(d) == "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
            {
                var cache = Path.Combine(profile, "Cache");
                if (Directory.Exists(cache)) found.Add(new CacheFolder(browser, process, cache));
            }
        }

        foreach (var (browser, folder) in new[] { ("Opera GX", "Opera GX Stable"), ("Opera", "Opera Stable") })
        {
            var cache = Path.Combine(local, "Opera Software", folder, "Cache");
            if (Directory.Exists(cache)) found.Add(new CacheFolder(browser, "opera", cache));
        }

        var firefoxProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
            foreach (var profile in SafeDirectories(firefoxProfiles))
            {
                var cache = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache)) found.Add(new CacheFolder("Firefox", "firefox", cache));
            }

        return found;
    }

    private static List<string> SafeDirectories(string path)
    {
        try { return Directory.GetDirectories(path).ToList(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return new List<string>(); }
    }

    private static long FolderBytes(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { /* a file that vanished or is locked */ }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { /* partial total is fine */ }
        return total;
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    public static CheckResult CheckBrowserCache()
    {
        const string techCommand = @"Get-ChildItem '$env:LOCALAPPDATA\Microsoft\Edge\User Data\*\Cache', '$env:LOCALAPPDATA\Google\Chrome\User Data\*\Cache', '$env:LOCALAPPDATA\Mozilla\Firefox\Profiles\*\cache2' -Recurse -File | Measure-Object Length -Sum";
        const long warnAtBytes = 2L << 30;
        try
        {
            var caches = FindBrowserCaches();
            if (caches.Count == 0)
                return new CheckResult(CheckStatus.Pass, "No browser cache folders were found for Edge, Chrome, Firefox, Brave or Opera.", techCommand, "None of the usual cache folders exist.");

            var sizes = caches.Select(c => (c.Browser, Bytes: FolderBytes(c.Path))).GroupBy(c => c.Browser)
                .Select(g => (Browser: g.Key, Bytes: g.Sum(x => x.Bytes))).OrderByDescending(x => x.Bytes).ToList();
            var total = sizes.Sum(s => s.Bytes);
            var breakdown = string.Join(", ", sizes.Where(s => s.Bytes > 0).Select(s => $"{s.Browser} {Size(s.Bytes)}"));
            var techDetail = $"Total {Size(total)} across {caches.Count} cache folder(s): " + string.Join(" | ", caches.Select(c => c.Path));

            return total >= warnAtBytes
                ? new CheckResult(CheckStatus.Warn, $"Your browsers are holding {Size(total)} of saved web files ({breakdown}). A bloated cache can make sites load wrong or not at all, and clearing it is safe.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Pass,
                    $"Your browsers' saved web files are a normal size ({Size(total)}{(breakdown.Length > 0 ? ": " + breakdown : "")}). If just one site won't load, try it in a private or incognito window: if it works there, that browser's saved data for the site is the cause.",
                    techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check your browsers' cache sizes.", techCommand, Detail(ex));
        }
    }

    /// <summary>
    /// SafeAutoFix - empties the cache folders of Edge, Chrome, Brave, Opera and Firefox. Only the cache: never
    /// passwords, cookies, history, bookmarks or logins. Refuses (with a clear message) while any of those browsers
    /// is open, because deleting files from a cache that is in use can corrupt it. A file that is still locked is skipped.
    /// </summary>
    public static Task<bool> ClearBrowserCachesAsync() => Task.Run(() =>
    {
        var caches = FindBrowserCaches();
        var open = caches.Where(c => Process.GetProcessesByName(c.ProcessName).Length > 0).Select(c => c.Browser).Distinct().ToList();
        if (open.Count > 0)
            throw new FixDeclinedException($"Close {string.Join(" and ", open)} first, then press this again. Clearing a browser's cache while it's open can corrupt it.");

        foreach (var cache in caches)
        {
            foreach (var file in Directory.EnumerateFiles(cache.Path))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* locked or protected - skip */ }
            }
            foreach (var dir in SafeDirectories(cache.Path))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use - skip */ }
            }
        }
        return true;
    });
}
