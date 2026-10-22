using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real, read-only network diagnostics: adapter/IP enumeration, DNS resolution, and ICMP
/// pings to the default gateway / a well-known public resolver. Pings and DNS lookups are
/// standard, safe network reads - nothing here writes to the system, changes adapter
/// settings, or touches anything that could brick hardware or trip BitLocker.
/// </summary>
public static class NetworkDiagnosticsService
{
    // Cloudflare's public resolver - stable, low-latency, no auth/account needed to probe it.
    private const string ProbeHost = "1.1.1.1";
    private const string DnsProbeName = "www.microsoft.com";

    /// <summary>The adapter actually carrying traffic (highest-speed adapter that's Up) - internal
    /// rather than private so WifiDiagnosticsService can reuse the same heuristic instead of
    /// re-implementing "which connection is really in use" a second time.</summary>
    internal static NetworkInterface? GetActiveAdapter() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(HasIPv4Gateway)
            .ThenBy(IsVirtualAdapter)
            .ThenByDescending(n => n.Speed)
            .FirstOrDefault();

    // "Carrying traffic" means having a default route out. Speed alone is a bad proxy: a Hyper-V
    // virtual switch reports 10 Gbps with no gateway and used to win over the real 1 Gbps NIC.
    private static bool HasIPv4Gateway(NetworkInterface n) =>
        n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                       && !g.Address.Equals(IPAddress.Any));

    // Ranks virtual/host-only adapters below real ones when no adapter has a gateway (e.g. DHCP
    // failed), so the diagnosis is about the real NIC rather than a vSwitch or Wi-Fi Direct adapter.
    private static bool IsVirtualAdapter(NetworkInterface n) =>
        new[] { "Virtual", "Hyper-V", "VMware", "VirtualBox", "Bluetooth", "TAP-Windows" }
            .Any(k => n.Description.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// `netsh interface show interface` reports Admin State (Enabled/Disabled - was it turned
    /// off) separately from State (Connected/Disconnected - is it actually linked up), which
    /// NetworkInterface.OperationalStatus alone can't cleanly distinguish. Used by
    /// CheckAdapterStatus to give a "can't get online at all" user a specific reason instead of
    /// just "no active adapter" - a genuinely important distinction on this card, since disabled
    /// vs. unplugged vs. missing entirely are three different fixes.
    /// </summary>
    private static List<(string AdminState, string State, string Name)> GetInterfaceStates()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = "interface show interface",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return new List<(string, string, string)>();

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        var rows = new List<(string, string, string)>();
        foreach (var line in output.Split('\n'))
        {
            var tokens = line.Trim().Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4) continue;
            if (tokens[0] is not ("Enabled" or "Disabled")) continue; // skips the header/dashes row
            rows.Add((tokens[0], tokens[1], tokens[3]));
        }
        return rows;
    }

    public static CheckResult CheckAdapterStatus()
    {
        var activeAdapter = GetActiveAdapter();
        var interfaces = GetInterfaceStates();
        const string techCommand = "netsh interface show interface";
        var techDetail = interfaces.Count == 0
            ? "No interfaces returned."
            : string.Join("; ", interfaces.Select(i => $"{i.Name}: {i.AdminState}/{i.State}"));

        if (activeAdapter is not null)
            return new CheckResult(CheckStatus.Pass, $"{activeAdapter.Name} is enabled and connected.", techCommand, techDetail);

        if (interfaces.Count == 0)
            return new CheckResult(CheckStatus.Bad, "No network adapters were detected on this system at all - that points to a missing or failed network driver, not a settings issue.", techCommand, "No interfaces returned by netsh.");

        var disabled = interfaces.Where(i => i.AdminState == "Disabled").Select(i => i.Name).ToList();
        if (disabled.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"{string.Join(", ", disabled)} {(disabled.Count == 1 ? "is" : "are")} turned off in Windows.", techCommand, techDetail);

        var disconnected = interfaces.Where(i => i.AdminState == "Enabled" && i.State != "Connected").Select(i => i.Name).ToList();
        if (disconnected.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"{string.Join(", ", disconnected)} {(disconnected.Count == 1 ? "is" : "are")} enabled but not connected - check the cable or Wi-Fi connection.", techCommand, techDetail);

        return new CheckResult(CheckStatus.Bad, "No active network adapter was found.", techCommand, techDetail);
    }

    public static CheckResult CheckIpAddress()
    {
        const string techCommand = "Get-NetIPAddress -AddressFamily IPv4";
        var adapter = GetActiveAdapter();
        if (adapter is null)
            return new CheckResult(CheckStatus.Bad, "No active adapter to check an IP address on.", techCommand, "No active adapter found.");

        var ipv4 = adapter.GetIPProperties().UnicastAddresses
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

        if (ipv4 is null)
            return new CheckResult(CheckStatus.Bad, "Your computer doesn't have a valid IP address.", techCommand, $"{adapter.Name}: no IPv4 address bound.");

        if (ipv4.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            return new CheckResult(CheckStatus.Bad, "Windows assigned a fallback address (169.254.x.x) - your router isn't handing out addresses.", techCommand, $"{adapter.Name}: {ipv4} (APIPA/link-local).");

        return new CheckResult(CheckStatus.Pass, $"Assigned {ipv4} on {adapter.Name}.", techCommand, $"{adapter.Name}: {ipv4}.");
    }

    public static CheckResult CheckGatewayReachable()
    {
        var adapter = GetActiveAdapter();
        var gateway = adapter?.GetIPProperties().GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

        if (gateway is null)
            return new CheckResult(CheckStatus.Warn, "No default gateway is configured.", "Get-NetRoute -DestinationPrefix '0.0.0.0/0'", "No gateway address found on the active adapter.");

        var gatewayText = gateway.ToString();
        var ms = MeasurePingMs(gatewayText, timeoutMs: 1000);
        var techCommand = $"Test-Connection {gatewayText} -Count 1";
        var techDetail = ms is null ? $"No reply from {gatewayText}." : $"Reply from {gatewayText}: {ms} ms.";

        return ms is not null
            ? new CheckResult(CheckStatus.Pass, "Your router is reachable.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Bad, "Your router isn't responding.", techCommand, techDetail);
    }

    public static CheckResult CheckServerReachable()
    {
        var ms = MeasurePingMs(ProbeHost, timeoutMs: 1000);
        var techCommand = $"Test-Connection {ProbeHost} -Count 1";
        var techDetail = ms is null ? $"No reply from {ProbeHost}." : $"Reply from {ProbeHost}: {ms} ms.";

        return ms is not null
            ? new CheckResult(CheckStatus.Pass, "The remote server is responding.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Bad, "Couldn't reach the internet - the connection may be down.", techCommand, techDetail);
    }

    public static CheckResult CheckPacketLoss()
    {
        const int attempts = 4;
        var lost = 0;
        var rtts = new List<long>();
        for (var i = 0; i < attempts; i++)
        {
            var ms = MeasurePingMs(ProbeHost, timeoutMs: 1000);
            if (ms is null) lost++;
            else rtts.Add(ms.Value);
        }

        var techCommand = $"Test-Connection {ProbeHost} -Count {attempts}";
        var techDetail = rtts.Count == 0
            ? $"{lost}/{attempts} replies received."
            : $"{attempts - lost}/{attempts} replies received. RTT (ms): {string.Join(", ", rtts)}.";

        var lossPercent = 100.0 * lost / attempts;
        return lossPercent switch
        {
            0 => new CheckResult(CheckStatus.Pass, "No packet loss detected on your connection.", techCommand, techDetail),
            <= 25 => new CheckResult(CheckStatus.Warn, $"Lost {lost} of {attempts} test packets - some instability detected.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Lost {lost} of {attempts} test packets - your connection is dropping frequently.", techCommand, techDetail),
        };
    }

    public static CheckResult CheckDnsResolution()
    {
        var techCommand = $"Resolve-DnsName {DnsProbeName}";
        try
        {
            var sw = Stopwatch.StartNew();
            var addresses = Dns.GetHostAddresses(DnsProbeName);
            sw.Stop();

            if (addresses.Length == 0)
                return new CheckResult(CheckStatus.Bad, "DNS didn't return an address for a known site.", techCommand, "0 addresses returned.");

            var techDetail = $"{sw.ElapsedMilliseconds} ms, {addresses.Length} address(es): {string.Join(", ", addresses.Select(a => a.ToString()))}";

            return sw.ElapsedMilliseconds switch
            {
                < 300 => new CheckResult(CheckStatus.Pass, "Your DNS is responding quickly.", techCommand, techDetail),
                < 1000 => new CheckResult(CheckStatus.Warn, $"DNS took {sw.ElapsedMilliseconds} ms to respond - slower than expected.", techCommand, techDetail),
                _ => new CheckResult(CheckStatus.Bad, $"DNS took over {sw.ElapsedMilliseconds} ms to respond - pages will feel slow to load.", techCommand, techDetail),
            };
        }
        catch (SocketException ex)
        {
            return new CheckResult(CheckStatus.Bad, "DNS lookups are failing - websites won't resolve.", techCommand, $"SocketException: {ex.Message}");
        }
    }

    /// <summary>
    /// Real check for whether Windows Firewall is on for the active network profile
    /// (`netsh advfirewall show currentprofile state`). Scoped honestly to "is the firewall on"
    /// rather than "is this specific app/connection being blocked" - answering the latter would
    /// need enumerating and matching individual firewall rules, which isn't a safe instant read.
    /// </summary>
    public static CheckResult CheckFirewallStatus()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = "advfirewall show currentprofile state",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't check the firewall's status.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);

        var stateLine = output.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("State", StringComparison.OrdinalIgnoreCase));
        if (stateLine is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't determine the firewall's current status.");

        if (stateLine.Contains("OFF", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Bad, "Windows Firewall is turned off for your current network - your connection isn't protected.");

        return stateLine.Contains("ON", StringComparison.OrdinalIgnoreCase)
            ? new CheckResult(CheckStatus.Pass, "Windows Firewall is on and active.")
            : new CheckResult(CheckStatus.Warn, "Couldn't determine the firewall's current status.");
    }

    /// <summary>
    /// SafeAutoFix - runs the built-in `ipconfig /flushdns`. Only clears a client-side cache,
    /// nothing persistent or reversible-needing.
    /// </summary>
    public static async Task<bool> FlushDnsAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ipconfig",
            Arguments = "/flushdns",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    /// <summary>
    /// SafeAutoFix - releases and re-requests the DHCP lease on the active adapter via
    /// `ipconfig /renew`. Self-correcting (worst case the adapter just gets the same lease
    /// back), so it doesn't need an undo - matches the brief's "adapter reset" whitelist entry.
    /// </summary>
    public static async Task<bool> RenewIpAsync()
    {
        var adapter = GetActiveAdapter();
        var psi = new ProcessStartInfo
        {
            FileName = "ipconfig",
            Arguments = adapter is null ? "/renew" : $"/renew \"{adapter.Name}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static long? MeasurePingMs(string host, int timeoutMs = 1500)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, timeoutMs);
            return reply?.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }

    /// <summary>
    /// Only meaningful on a wired connection - a Gigabit-capable port that negotiated down to
    /// 100 Mbps (bad cable, loose connection, an old switch/router port) is a genuinely common,
    /// otherwise-invisible cause of "everything feels slow". Deliberately scoped to Ethernet
    /// only: Wi-Fi's "expected" link rate varies hugely by router/adapter generation, so there's
    /// no honest fixed threshold to compare against the way there is for wired (same reasoning
    /// as CPU temperature/Wi-Fi power-saving being left unwired elsewhere in this app - a
    /// confidently wrong number is worse than not checking). Wi-Fi quality is covered instead by
    /// the signal-strength check on this same card.
    /// </summary>
    public static CheckResult CheckAdapterLinkSpeed()
    {
        var adapter = GetActiveAdapter();
        if (adapter is null)
            return new CheckResult(CheckStatus.Warn, "No active adapter to check link speed on.");

        if (adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
            return new CheckResult(CheckStatus.Pass, "This check applies to wired connections - your Wi-Fi quality is covered by the signal strength check instead.");

        var mbps = adapter.Speed / 1_000_000;
        var techCommand = "Get-NetAdapter | Select-Object Name, LinkSpeed";
        var techDetail = $"{adapter.Name}: {adapter.Speed:N0} bps ({mbps:N0} Mbps)";
        return mbps switch
        {
            >= 1000 => new CheckResult(CheckStatus.Pass, $"Your Ethernet connection is running at full speed ({mbps} Mbps).", techCommand, techDetail),
            >= 100 => new CheckResult(CheckStatus.Warn, $"Your Ethernet connection is only running at {mbps} Mbps, well below the 1 Gbps modern hardware supports - this is very likely capping your speeds.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Your Ethernet connection is running at only {mbps} Mbps - far below what modern hardware supports.", techCommand, techDetail),
        };
    }

    /// <summary>
    /// Reads the per-user proxy setting Windows itself uses for all connections
    /// (Settings > Network & Internet > Proxy writes here). A stale or slow proxy silently makes
    /// everything feel sluggish and is invisible to a non-technical user. Deliberately scoped to
    /// just this registry read, not also guessing at VPN adapters by name/type pattern
    /// (TAP/WireGuard/etc. naming isn't standardized enough to detect reliably) - same "don't
    /// ship an unreliable heuristic" reasoning as elsewhere in this app.
    /// </summary>
    public static CheckResult CheckProxyConfiguration()
    {
        var techCommand = "Get-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings'";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var enabled = key?.GetValue("ProxyEnable") is int i && i != 0;
            if (!enabled)
                return new CheckResult(CheckStatus.Pass, "No proxy server is configured - your connection goes straight out.", techCommand, "ProxyEnable = 0");

            var server = key?.GetValue("ProxyServer") as string;
            var techDetail = $"ProxyEnable = 1, ProxyServer = {(string.IsNullOrEmpty(server) ? "(empty)" : server)}";
            return new CheckResult(CheckStatus.Warn,
                string.IsNullOrEmpty(server)
                    ? "A proxy server is configured for your connection - this can slow things down significantly if it wasn't set up intentionally."
                    : $"A proxy server ({server}) is configured for your connection - this can slow things down significantly if it wasn't set up intentionally.",
                techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check your proxy configuration.", techCommand, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// "Bufferbloat" - a router's outgoing queue filling up under load and delaying everything
    /// else, the classic cause of choppy video calls or laggy games specifically when someone
    /// else on the connection starts a big download/stream. Measures idle ping latency, then
    /// starts a background download and measures ping again while it's running - a healthy
    /// connection's latency barely moves; a bufferbloated one spikes heavily. The load generator
    /// reuses the same Cloudflare speed-test endpoint SpeedTestService already trusts, just with
    /// a larger byte cap so it keeps the link busy for the couple of seconds this needs.
    /// </summary>
    public static CheckResult CheckLatencyUnderLoad()
    {
        var baseline = AveragePingMs(ProbeHost, samples: 4);
        if (baseline is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't measure baseline latency to test this.");

        using var loadStop = new CancellationTokenSource();
        var loadTask = Task.Run(() => GenerateBriefLoad(loadStop.Token));
        Thread.Sleep(400); // let the download ramp up before measuring against it

        var underLoad = AveragePingMs(ProbeHost, samples: 4);
        loadStop.Cancel();
        try { loadTask.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort - the check result doesn't depend on the load task finishing cleanly */ }

        if (underLoad is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't measure latency under load to test this.");

        var increase = underLoad.Value - baseline.Value;
        var techCommand = $"Test-Connection {ProbeHost} -Count 4 (idle, then again during a background download)";
        var techDetail = $"Idle avg: {baseline} ms. Under load avg: {underLoad} ms. Increase: {increase} ms.";

        return increase switch
        {
            < 50 => new CheckResult(CheckStatus.Pass, $"Latency stays steady under load ({baseline} ms idle, {underLoad} ms while downloading) - no bufferbloat detected.", techCommand, techDetail),
            < 150 => new CheckResult(CheckStatus.Warn, $"Latency rises under load ({baseline} ms idle → {underLoad} ms while downloading) - this can make video calls or games choppy when your connection is busy.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Latency spikes heavily under load ({baseline} ms idle → {underLoad} ms while downloading) - a classic sign of bufferbloat.", techCommand, techDetail),
        };
    }

    /// <summary>
    /// Pings the router and a public host the same number of times and compares the loss, to
    /// tell "your PC can't hold a steady link to the router" (Wi-Fi signal, router, adapter)
    /// apart from "the router's fine but the connection beyond it is dropping" (modem, ISP) -
    /// the single most useful question for an intermittent-drop complaint, with different fixes.
    /// </summary>
    public static CheckResult CheckLinkStability()
    {
        const int samples = 10;
        var adapter = GetActiveAdapter();
        var gateway = adapter?.GetIPProperties().GatewayAddresses
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();

        if (gateway is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't find your router to test how steady the connection to it is.",
                "Get-NetRoute -DestinationPrefix '0.0.0.0/0'", "No gateway address found on the active adapter.");

        List<long?> Sample(string host)
        {
            var results = new List<long?>();
            for (var i = 0; i < samples; i++)
            {
                results.Add(MeasurePingMs(host, timeoutMs: 800));
                Thread.Sleep(100);
            }
            return results;
        }

        var toRouter = Sample(gateway);
        var toInternet = Sample(ProbeHost);
        var routerLost = toRouter.Count(r => r is null);
        var internetLost = toInternet.Count(r => r is null);
        var routerMax = toRouter.Where(r => r is not null).Select(r => r!.Value).DefaultIfEmpty(0).Max();

        var techCommand = $"Test-Connection {gateway} -Count {samples}; Test-Connection {ProbeHost} -Count {samples}";
        var techDetail = $"Router {gateway}: {samples - routerLost}/{samples} replies, RTTs (ms): {string.Join(", ", toRouter.Select(r => r?.ToString() ?? "lost"))}. " +
                         $"{ProbeHost}: {samples - internetLost}/{samples} replies, RTTs (ms): {string.Join(", ", toInternet.Select(r => r?.ToString() ?? "lost"))}.";

        // A single lost ping in 10 is ordinary noise (ICMP is rate-limited and deprioritized by many
        // routers and hosts), so one stray loss passes; two or more is a real pattern. Verified live:
        // flagging 1 of 10 as "your internet is dropping" cried wolf on a healthy wired connection.
        if (routerLost >= 2)
            return new CheckResult(routerLost >= 4 ? CheckStatus.Bad : CheckStatus.Warn,
                $"Lost {routerLost} of {samples} test packets just reaching your router - the drops are between your PC and the router, not your internet provider.", techCommand, techDetail);
        if (internetLost >= 2)
            return new CheckResult(internetLost >= 4 ? CheckStatus.Bad : CheckStatus.Warn,
                $"Your router answered the tests, but {internetLost} of {samples} packets to the internet were lost - the drops are beyond your router (modem or internet provider).", techCommand, techDetail);
        if (routerMax > 100)
            return new CheckResult(CheckStatus.Warn,
                $"No packets were lost, but replies from your router spiked up to {routerMax} ms - the link to it isn't steady.", techCommand, techDetail);

        return routerLost + internetLost == 0
            ? new CheckResult(CheckStatus.Pass, $"All {samples} test packets reached both your router and the internet - the connection held steady.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Pass, $"Nearly every test packet got through (only {routerLost + internetLost} of {samples * 2} lost, which is normal) - the connection held steady.", techCommand, techDetail);
    }

    private static long? AveragePingMs(string host, int samples)
    {
        var results = new List<long>();
        for (var i = 0; i < samples; i++)
        {
            var ms = MeasurePingMs(host);
            if (ms is not null) results.Add(ms.Value);
        }
        return results.Count == 0 ? null : (long)results.Average();
    }

    private static void GenerateBriefLoad(CancellationToken token)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.Send(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=25000000"), token);
            using var stream = response.Content.ReadAsStream(token);
            var buffer = new byte[81920];
            while (!token.IsCancellationRequested && stream.Read(buffer, 0, buffer.Length) > 0) { }
        }
        catch
        {
            // Cancellation or a network hiccup mid-download just means the load generator stops
            // early - the check still works, it just measured against less load than intended.
        }
    }
}
