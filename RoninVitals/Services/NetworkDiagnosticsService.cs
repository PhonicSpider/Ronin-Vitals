using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only network checks (adapter, IP, router, DNS, speed of the link, proxy, stability) plus two small fixes.</summary>
public static class NetworkDiagnosticsService
{
    private const string ProbeHost = "1.1.1.1"; // Cloudflare's public resolver
    private const string DnsProbeName = "www.microsoft.com";
    private const string LoadDownloadUrl = "https://speed.cloudflare.com/__down?bytes=25000000";

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan IpRenewTimeout = TimeSpan.FromSeconds(90); // DHCP can take a while to give up

    private sealed record InterfaceState(string AdminState, string State, string Name);

    // ── Adapter and address ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>netsh</c> reports "Enabled/Disabled" separately from "Connected/Disconnected", which the .NET adapter status cannot,
    /// so a dead connection can be explained as turned off, unplugged, or missing.
    /// </summary>
    private static List<InterfaceState> ReadInterfaceStates()
    {
        var output = ProcessRunner.Run("netsh", "interface show interface", ToolTimeout).Output;

        var states = new List<InterfaceState>();
        foreach (var line in output.Split('\n'))
        {
            var columns = line.Trim().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 4 || columns[0] is not ("Enabled" or "Disabled")) continue; // skips the header rows
            states.Add(new InterfaceState(AdminState: columns[0], State: columns[1], Name: columns[3]));
        }
        return states;
    }

    public static CheckResult CheckAdapterStatus()
    {
        const string techCommand = "netsh interface show interface";
        var activeAdapter = ActiveAdapter.Find();
        var interfaces = ReadInterfaceStates();
        var techDetail = interfaces.Count == 0
            ? "No interfaces returned."
            : string.Join("; ", interfaces.Select(state => $"{state.Name}: {state.AdminState}/{state.State}"));

        if (activeAdapter is not null)
            return new CheckResult(CheckStatus.Pass, $"{activeAdapter.Name} is enabled and connected.", techCommand, techDetail);

        if (interfaces.Count == 0)
            return new CheckResult(CheckStatus.Bad, "No network adapters were detected on this system at all - that points to a missing or failed network driver, not a settings issue.", techCommand, "No interfaces returned by netsh.");

        var disabled = interfaces.Where(state => state.AdminState == "Disabled").Select(state => state.Name).ToList();
        if (disabled.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"{string.Join(", ", disabled)} {(disabled.Count == 1 ? "is" : "are")} turned off in Windows.", techCommand, techDetail);

        var disconnected = interfaces.Where(state => state.AdminState == "Enabled" && state.State != "Connected").Select(state => state.Name).ToList();
        if (disconnected.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"{string.Join(", ", disconnected)} {(disconnected.Count == 1 ? "is" : "are")} enabled but not connected - check the cable or Wi-Fi connection.", techCommand, techDetail);

        return new CheckResult(CheckStatus.Bad, "No active network adapter was found.", techCommand, techDetail);
    }

    public static CheckResult CheckIpAddress()
    {
        const string techCommand = "Get-NetIPAddress -AddressFamily IPv4";
        var adapter = ActiveAdapter.Find();
        if (adapter is null)
            return new CheckResult(CheckStatus.Bad, "No active adapter to check an IP address on.", techCommand, "No active adapter found.");

        var address = ActiveAdapter.FindIPv4Address(adapter);
        if (address is null)
            return new CheckResult(CheckStatus.Bad, "Your computer doesn't have a valid IP address.", techCommand, $"{adapter.Name}: no IPv4 address bound.");

        if (address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            return new CheckResult(CheckStatus.Bad, "Windows assigned a fallback address (169.254.x.x) - your router isn't handing out addresses.", techCommand, $"{adapter.Name}: {address} (APIPA/link-local).");

        return new CheckResult(CheckStatus.Pass, $"Assigned {address} on {adapter.Name}.", techCommand, $"{adapter.Name}: {address}.");
    }

    // ── Reachability ────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckGatewayReachable()
    {
        var gateway = ActiveAdapter.FindIPv4Gateway(ActiveAdapter.Find());
        if (gateway is null)
            return new CheckResult(CheckStatus.Warn, "No default gateway is configured.", "Get-NetRoute -DestinationPrefix '0.0.0.0/0'", "No gateway address found on the active adapter.");

        var roundTrip = PingProbe.MeasureMilliseconds(gateway.ToString(), timeoutMilliseconds: 1000);
        var techCommand = $"Test-Connection {gateway} -Count 1";
        var techDetail = roundTrip is null ? $"No reply from {gateway}." : $"Reply from {gateway}: {roundTrip} ms.";

        return roundTrip is not null
            ? new CheckResult(CheckStatus.Pass, "Your router is reachable.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Bad, "Your router isn't responding.", techCommand, techDetail);
    }

    public static CheckResult CheckServerReachable()
    {
        var roundTrip = PingProbe.MeasureMilliseconds(ProbeHost, timeoutMilliseconds: 1000);
        var techCommand = $"Test-Connection {ProbeHost} -Count 1";
        var techDetail = roundTrip is null ? $"No reply from {ProbeHost}." : $"Reply from {ProbeHost}: {roundTrip} ms.";

        return roundTrip is not null
            ? new CheckResult(CheckStatus.Pass, "The remote server is responding.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Bad, "Couldn't reach the internet - the connection may be down.", techCommand, techDetail);
    }

    public static CheckResult CheckPacketLoss()
    {
        const int attempts = 4;
        var roundTrips = PingProbe.Sample(ProbeHost, attempts, timeoutMilliseconds: 1000);
        var received = roundTrips.OfType<long>().ToList();
        var lost = attempts - received.Count;

        var techCommand = $"Test-Connection {ProbeHost} -Count {attempts}";
        var techDetail = received.Count == 0
            ? $"{received.Count}/{attempts} replies received."
            : $"{received.Count}/{attempts} replies received. RTT (ms): {string.Join(", ", received)}.";

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
            var stopwatch = Stopwatch.StartNew();
            var addresses = Dns.GetHostAddresses(DnsProbeName);
            stopwatch.Stop();

            if (addresses.Length == 0)
                return new CheckResult(CheckStatus.Bad, "DNS didn't return an address for a known site.", techCommand, "0 addresses returned.");

            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            var techDetail = $"{elapsedMilliseconds} ms, {addresses.Length} address(es): {string.Join(", ", addresses.Select(address => address.ToString()))}";

            return elapsedMilliseconds switch
            {
                < 300 => new CheckResult(CheckStatus.Pass, "Your DNS is responding quickly.", techCommand, techDetail),
                < 1000 => new CheckResult(CheckStatus.Warn, $"DNS took {elapsedMilliseconds} ms to respond - slower than expected.", techCommand, techDetail),
                _ => new CheckResult(CheckStatus.Bad, $"DNS took over {elapsedMilliseconds} ms to respond - pages will feel slow to load.", techCommand, techDetail),
            };
        }
        catch (SocketException exception)
        {
            return new CheckResult(CheckStatus.Bad, "DNS lookups are failing - websites won't resolve.", techCommand, $"SocketException: {exception.Message}");
        }
    }

    // ── Fixes ───────────────────────────────────────────────────────────────────────────────

    /// <summary>SafeAutoFix: clears the DNS resolver cache (<c>ipconfig /flushdns</c>). Only a client-side cache.</summary>
    public static Task<bool> FlushDnsAsync() =>
        Task.Run(() => ProcessRunner.Run("ipconfig", "/flushdns", ToolTimeout).Succeeded);

    /// <summary>SafeAutoFix: asks for a fresh DHCP lease on the active adapter (<c>ipconfig /renew</c>). Worst case it gets the same lease back.</summary>
    public static Task<bool> RenewIpAsync() => Task.Run(() =>
    {
        var adapter = ActiveAdapter.Find();
        var arguments = adapter is null ? "/renew" : $"/renew \"{adapter.Name}\"";
        return ProcessRunner.Run("ipconfig", arguments, IpRenewTimeout).Succeeded;
    });

    // ── Link quality ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wired only: a gigabit port that negotiated down to 100 Mbps (bad cable, loose plug, old switch port) caps every speed.
    /// Wi-Fi has no fixed "expected" rate, so it is covered by the signal-strength check instead.
    /// </summary>
    public static CheckResult CheckAdapterLinkSpeed()
    {
        var adapter = ActiveAdapter.Find();
        if (adapter is null)
            return new CheckResult(CheckStatus.Warn, "No active adapter to check link speed on.");

        if (adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
            return new CheckResult(CheckStatus.Pass, "This check applies to wired connections - your Wi-Fi quality is covered by the signal strength check instead.");

        var megabitsPerSecond = adapter.Speed / 1_000_000;
        const string techCommand = "Get-NetAdapter | Select-Object Name, LinkSpeed";
        var techDetail = $"{adapter.Name}: {adapter.Speed:N0} bps ({megabitsPerSecond:N0} Mbps)";
        return megabitsPerSecond switch
        {
            >= 1000 => new CheckResult(CheckStatus.Pass, $"Your Ethernet connection is running at full speed ({megabitsPerSecond} Mbps).", techCommand, techDetail),
            >= 100 => new CheckResult(CheckStatus.Warn, $"Your Ethernet connection is only running at {megabitsPerSecond} Mbps, well below the 1 Gbps modern hardware supports - this is very likely capping your speeds.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Your Ethernet connection is running at only {megabitsPerSecond} Mbps - far below what modern hardware supports.", techCommand, techDetail),
        };
    }

    /// <summary>Reads the proxy setting Windows applies to all connections. A stale proxy slows or breaks everything and is invisible to most users.</summary>
    public static CheckResult CheckProxyConfiguration()
    {
        const string techCommand = @"Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'";
        try
        {
            using var internetSettings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var proxyIsEnabled = internetSettings?.GetValue("ProxyEnable") is int proxyEnableValue && proxyEnableValue != 0;
            if (!proxyIsEnabled)
                return new CheckResult(CheckStatus.Pass, "No proxy server is configured - your connection goes straight out.", techCommand, "ProxyEnable = 0");

            var proxyServer = internetSettings?.GetValue("ProxyServer") as string;
            var techDetail = $"ProxyEnable = 1, ProxyServer = {(string.IsNullOrEmpty(proxyServer) ? "(empty)" : proxyServer)}";
            return new CheckResult(CheckStatus.Warn,
                string.IsNullOrEmpty(proxyServer)
                    ? "A proxy server is configured for your connection - this can slow things down significantly if it wasn't set up intentionally."
                    : $"A proxy server ({proxyServer}) is configured for your connection - this can slow things down significantly if it wasn't set up intentionally.",
                techCommand, techDetail);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check your proxy configuration.", techCommand, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Bufferbloat: a router's queue filling up under load and delaying everything else. Compares ping latency while idle
    /// with latency during a background download; a healthy link barely moves.
    /// </summary>
    public static CheckResult CheckLatencyUnderLoad()
    {
        const int sampleCount = 4;
        var idleAverage = PingProbe.AverageMilliseconds(PingProbe.Sample(ProbeHost, sampleCount, timeoutMilliseconds: 1500));
        if (idleAverage is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't measure baseline latency to test this.");

        using var stopLoad = new CancellationTokenSource();
        var loadTask = Task.Run(() => GenerateBriefLoad(stopLoad.Token));
        Thread.Sleep(400); // let the download ramp up first

        var loadedAverage = PingProbe.AverageMilliseconds(PingProbe.Sample(ProbeHost, sampleCount, timeoutMilliseconds: 1500));
        stopLoad.Cancel();
        loadTask.Wait(TimeSpan.FromSeconds(2)); // best effort; GenerateBriefLoad never throws

        if (loadedAverage is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't measure latency under load to test this.");

        var increase = loadedAverage.Value - idleAverage.Value;
        var techCommand = $"Test-Connection {ProbeHost} -Count {sampleCount} (idle, then again during a background download)";
        var techDetail = $"Idle avg: {idleAverage} ms. Under load avg: {loadedAverage} ms. Increase: {increase} ms.";

        return increase switch
        {
            < 50 => new CheckResult(CheckStatus.Pass, $"Latency stays steady under load ({idleAverage} ms idle, {loadedAverage} ms while downloading) - no bufferbloat detected.", techCommand, techDetail),
            < 150 => new CheckResult(CheckStatus.Warn, $"Latency rises under load ({idleAverage} ms idle → {loadedAverage} ms while downloading) - this can make video calls or games choppy when your connection is busy.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Latency spikes heavily under load ({idleAverage} ms idle → {loadedAverage} ms while downloading) - a classic sign of bufferbloat.", techCommand, techDetail),
        };
    }

    /// <summary>Ping the router and a public host equally often. Loss to the router means the PC-to-router link is the problem; loss only beyond it means the modem or provider.</summary>
    public static CheckResult CheckLinkStability()
    {
        const int sampleCount = 10;
        const int strayLossTolerated = 1;   // one lost ping in ten is ordinary ICMP noise
        const int badLossCount = 4;
        const int spikeThresholdMilliseconds = 100;

        var gateway = ActiveAdapter.FindIPv4Gateway(ActiveAdapter.Find());
        if (gateway is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't find your router to test how steady the connection to it is.",
                "Get-NetRoute -DestinationPrefix '0.0.0.0/0'", "No gateway address found on the active adapter.");

        var routerRoundTrips = PingProbe.Sample(gateway.ToString(), sampleCount, timeoutMilliseconds: 800, pauseMilliseconds: 100);
        var internetRoundTrips = PingProbe.Sample(ProbeHost, sampleCount, timeoutMilliseconds: 800, pauseMilliseconds: 100);
        var routerLost = routerRoundTrips.Count(roundTrip => roundTrip is null);
        var internetLost = internetRoundTrips.Count(roundTrip => roundTrip is null);
        var slowestRouterReply = routerRoundTrips.OfType<long>().DefaultIfEmpty(0).Max();

        var techCommand = $"Test-Connection {gateway} -Count {sampleCount}; Test-Connection {ProbeHost} -Count {sampleCount}";
        var techDetail = $"Router {gateway}: {sampleCount - routerLost}/{sampleCount} replies, RTTs (ms): {FormatRoundTrips(routerRoundTrips)}. " +
                         $"{ProbeHost}: {sampleCount - internetLost}/{sampleCount} replies, RTTs (ms): {FormatRoundTrips(internetRoundTrips)}.";

        if (routerLost > strayLossTolerated)
            return new CheckResult(routerLost >= badLossCount ? CheckStatus.Bad : CheckStatus.Warn,
                $"Lost {routerLost} of {sampleCount} test packets just reaching your router - the drops are between your PC and the router, not your internet provider.", techCommand, techDetail);
        if (internetLost > strayLossTolerated)
            return new CheckResult(internetLost >= badLossCount ? CheckStatus.Bad : CheckStatus.Warn,
                $"Your router answered the tests, but {internetLost} of {sampleCount} packets to the internet were lost - the drops are beyond your router (modem or internet provider).", techCommand, techDetail);
        if (slowestRouterReply > spikeThresholdMilliseconds)
            return new CheckResult(CheckStatus.Warn,
                $"No packets were lost, but replies from your router spiked up to {slowestRouterReply} ms - the link to it isn't steady.", techCommand, techDetail);

        var totalLost = routerLost + internetLost;
        return totalLost == 0
            ? new CheckResult(CheckStatus.Pass, $"All {sampleCount} test packets reached both your router and the internet - the connection held steady.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Pass, $"Nearly every test packet got through (only {totalLost} of {sampleCount * 2} lost, which is normal) - the connection held steady.", techCommand, techDetail);
    }

    private static string FormatRoundTrips(IEnumerable<long?> roundTrips) =>
        string.Join(", ", roundTrips.Select(roundTrip => roundTrip?.ToString() ?? "lost"));

    private static void GenerateBriefLoad(CancellationToken cancellation)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, LoadDownloadUrl), cancellation);
            using var stream = response.Content.ReadAsStream(cancellation);
            var buffer = new byte[81920];
            while (!cancellation.IsCancellationRequested && stream.Read(buffer, 0, buffer.Length) > 0) { }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // The load simply stops early; the check still measures, against less load.
        }
    }
}
