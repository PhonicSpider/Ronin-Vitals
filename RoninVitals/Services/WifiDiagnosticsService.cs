using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real Wi-Fi checks via `netsh wlan` output parsing (no public .NET API surfaces signal
/// strength, channel, or nearby-network data) plus WMI for driver age. All read-only - nothing
/// here changes adapter settings, radio state, or power management.
///
/// Wi-Fi power-saving status is deliberately NOT wired here, even as a read-only check. The
/// registry mechanism for it (a PnPCapabilities bit under the adapter's device-class key) isn't
/// officially documented, and sources disagree enough on the exact bit meaning that a
/// "definitely correct" read isn't achievable with confidence - same reasoning as the
/// CPU-temperature check in SystemDiagnosticsService: a confidently wrong answer is worse than
/// a placeholder.
/// </summary>
public static class WifiDiagnosticsService
{
    private static readonly Regex ChannelLine = new(@"Channel\s*:\s*(\d+)", RegexOptions.Compiled);

    private static string RunNetsh(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return string.Empty;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }

    private static Dictionary<string, string> ParseInterfaceInfo(string output)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 1) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || info.ContainsKey(key)) continue;
            info[key] = value;
        }
        return info;
    }

    private static bool NoWirelessInterface(string output) =>
        output.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase);

    public static CheckResult CheckSignalStrength()
    {
        const string techCommand = "netsh wlan show interfaces";
        var output = RunNetsh("wlan show interfaces");
        if (NoWirelessInterface(output))
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi adapter found on this system - nothing to check here.", techCommand, "No wireless interface present.");

        var info = ParseInterfaceInfo(output);
        if (!info.TryGetValue("State", out var state) || !state.Equals("connected", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Warn, "Not currently connected to a Wi-Fi network.", techCommand, $"State = {state ?? "(not reported)"}");

        if (!info.TryGetValue("Signal", out var signalText) || !int.TryParse(signalText.TrimEnd('%'), out var signal))
            return new CheckResult(CheckStatus.Warn, "Couldn't read Wi-Fi signal strength.", techCommand, "No Signal field in netsh output.");

        var techDetail = $"Signal = {signal}%";
        return signal switch
        {
            >= 70 => new CheckResult(CheckStatus.Pass, $"Signal is strong ({signal}%).", techCommand, techDetail),
            >= 40 => new CheckResult(CheckStatus.Warn, $"Signal is weaker than expected for this room ({signal}%).", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Signal is very weak ({signal}%) - move closer to the router.", techCommand, techDetail),
        };
    }

    /// <summary>
    /// Wraps <see cref="CheckSignalStrength"/> for reuse on the "internet feels slow" card,
    /// which unlike "Wi-Fi keeps dropping" isn't Wi-Fi-specific - a machine on Ethernet doesn't
    /// have a Wi-Fi problem to report. Verified live: on an Ethernet-only dev machine,
    /// `CheckSignalStrength` alone showed a misleading Warn ("Not currently connected to a
    /// Wi-Fi network"), which reads as something wrong even though the user isn't using Wi-Fi at
    /// all. This checks the active adapter's type first (same pattern as
    /// <see cref="CheckNetworkCongestion"/>) and only delegates to the real signal-strength
    /// check when the active connection is genuinely Wi-Fi - `CheckSignalStrength` itself stays
    /// unchanged since "not connected to Wi-Fi" is legitimately Warn-worthy on the card that's
    /// explicitly about Wi-Fi problems.
    /// </summary>
    public static CheckResult CheckSignalStrengthIfActive()
    {
        var activeAdapter = NetworkDiagnosticsService.GetActiveAdapter();
        if (activeAdapter is not null && activeAdapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
        {
            var connectionType = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "a wired connection";
            return new CheckResult(CheckStatus.Pass, $"You're connected over {connectionType}, not Wi-Fi - signal strength doesn't apply here.",
                "Get-NetAdapter | Where-Object Status -eq Up", $"{activeAdapter.Name}: {activeAdapter.NetworkInterfaceType}");
        }
        return CheckSignalStrength();
    }

    /// <summary>
    /// Was Wi-Fi-only - always ran `netsh wlan` regardless of how the machine was actually
    /// connected, so an Ethernet-only user on the "internet feels slow" card saw "Couldn't
    /// determine which Wi-Fi channel you're on," which reads as a failure when the honest
    /// answer is "channel congestion doesn't apply to you at all." Now checks which adapter is
    /// actually carrying traffic first (the same heuristic NetworkDiagnosticsService uses
    /// elsewhere) and only runs the Wi-Fi-specific check when that adapter is genuinely Wi-Fi.
    /// </summary>
    public static CheckResult CheckNetworkCongestion()
    {
        var activeAdapter = NetworkDiagnosticsService.GetActiveAdapter();
        if (activeAdapter is not null && activeAdapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
        {
            var connectionType = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "a wired connection";
            return new CheckResult(CheckStatus.Pass, $"You're connected over {connectionType}, not Wi-Fi - channel congestion doesn't apply here.",
                "Get-NetAdapter | Where-Object Status -eq Up", $"{activeAdapter.Name}: {activeAdapter.NetworkInterfaceType}");
        }

        const string techCommand = "netsh wlan show interfaces; netsh wlan show networks mode=bssid";
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (NoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi adapter found on this system - nothing to check here.", techCommand, "No wireless interface present.");

        var info = ParseInterfaceInfo(interfaceOutput);
        if (!info.TryGetValue("Channel", out var channelText) || !int.TryParse(channelText, out var currentChannel))
            return new CheckResult(CheckStatus.Warn, "Couldn't determine which Wi-Fi channel you're on.", techCommand, "No Channel field in netsh output.");

        var networksOutput = RunNetsh("wlan show networks mode=bssid");
        var channels = ChannelLine.Matches(networksOutput).Select(m => int.Parse(m.Groups[1].Value)).ToList();
        var sharing = channels.Count(c => c == currentChannel) - 1; // exclude the network you're on
        var techDetail = $"Your channel: {currentChannel}. Networks seen on that channel (incl. yours): {channels.Count(c => c == currentChannel)}.";

        return sharing switch
        {
            <= 0 => new CheckResult(CheckStatus.Pass, "No other networks sharing your Wi-Fi channel nearby.", techCommand, techDetail),
            <= 2 => new CheckResult(CheckStatus.Pass, $"{sharing} nearby network{(sharing == 1 ? "" : "s")} sharing your channel - not enough to worry about.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Warn, $"{sharing} nearby networks are sharing your Wi-Fi channel - that can cause interference.", techCommand, techDetail),
        };
    }

    public static CheckResult CheckDriverStatus()
    {
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (NoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi adapter found on this system - nothing to check here.");

        var info = ParseInterfaceInfo(interfaceOutput);
        if (!info.TryGetValue("Name", out var adapterName))
            return new CheckResult(CheckStatus.Warn, "Couldn't identify the Wi-Fi adapter to check its driver.");

        try
        {
            using var adapterSearcher = new ManagementObjectSearcher(
                $"SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE NetConnectionID = '{adapterName.Replace("'", "''")}'");
            var pnpDeviceId = adapterSearcher.Get().Cast<ManagementObject>().FirstOrDefault()?["PNPDeviceID"] as string;
            if (pnpDeviceId is null)
                return new CheckResult(CheckStatus.Warn, "Couldn't look up the Wi-Fi adapter's driver.");

            using var driverSearcher = new ManagementObjectSearcher(
                $"SELECT DriverDate FROM Win32_PnPSignedDriver WHERE DeviceID = '{pnpDeviceId.Replace("'", "''")}'");
            var driverDateRaw = driverSearcher.Get().Cast<ManagementObject>().FirstOrDefault()?["DriverDate"] as string;
            if (driverDateRaw is null)
                return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date.");

            var driverDate = ManagementDateTimeConverter.ToDateTime(driverDateRaw);
            var ageDays = (DateTime.Now - driverDate).TotalDays;

            return ageDays < 730
                ? new CheckResult(CheckStatus.Pass, $"Driver is from {driverDate:MMMM yyyy} - reasonably current.")
                : new CheckResult(CheckStatus.Warn, $"Driver hasn't been updated since {driverDate:MMMM yyyy} - a newer one may fix stability issues.");
        }
        catch (ManagementException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date on this system.");
        }
    }
}
