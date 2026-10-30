using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real Wi-Fi checks via `netsh wlan` output parsing (no public .NET API surfaces signal
/// strength, channel, or nearby-network data) plus WMI for driver age. The checks are all
/// read-only; the one fix here (<see cref="DisableWirelessPowerSavingAsync"/>) only changes the
/// active power plan's documented "Wireless Adapter Settings > Power Saving Mode" value, with an undo.
///
/// Wi-Fi power saving is read from that documented powercfg setting, NOT from the adapter's
/// PnPCapabilities registry bit, which isn't officially documented and which sources disagree on -
/// the earlier reason this check was left as a scripted placeholder. powercfg's index values are
/// documented (0 = Maximum Performance ... 3 = Maximum Power Saving), so a confident read is
/// possible after all.
/// </summary>
public static class WifiDiagnosticsService
{
    private static readonly Regex ChannelLine = new(@"Channel\s*:\s*(\d+)", RegexOptions.Compiled);

    // powercfg GUIDs for "Wireless Adapter Settings" and its "Power Saving Mode" setting.
    private const string WirelessSubgroup = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string WirelessPowerSetting = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    private static readonly Regex AcIndex = new(@"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    private static readonly Regex DcIndex = new(@"Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    private static readonly string[] PowerModeNames = { "Maximum Performance", "Low Power Saving", "Medium Power Saving", "Maximum Power Saving" };

    /// <summary>The AC/DC values captured right before the fix changed them, so Undo can restore exactly them.</summary>
    private static (int Ac, int Dc)? _powerIndexesBeforeFix;

    private static (int ExitCode, string Output) RunTool(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return (-1, string.Empty);

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return (process.HasExited ? process.ExitCode : -1, output);
    }

    private static string RunNetsh(string args) => RunTool("netsh", args).Output;

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

    /// <summary>Extra raw link details from `netsh wlan show interfaces` (SSID, band, radio type,
    /// channel, rates) appended to the technician-facing detail line only - never the friendly text.</summary>
    private static string DescribeLink(Dictionary<string, string> info)
    {
        var parts = new List<string>();
        foreach (var key in new[] { "SSID", "Band", "Radio type", "Channel", "Receive rate (Mbps)", "Transmit rate (Mbps)" })
            if (info.TryGetValue(key, out var value) && value.Length > 0) parts.Add($"{key} = {value}");
        return parts.Count == 0 ? string.Empty : ". " + string.Join(", ", parts);
    }

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

        var techDetail = $"Signal = {signal}%{DescribeLink(info)}";
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
        var activeAdapter = ActiveAdapter.Find();
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
        var activeAdapter = ActiveAdapter.Find();
        if (activeAdapter is not null && activeAdapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
        {
            var connectionType = activeAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "a wired connection";
            return new CheckResult(CheckStatus.Pass, $"You're connected over {connectionType}, not Wi-Fi - channel congestion doesn't apply here.",
                "Get-NetAdapter | Where-Object Status -eq Up", $"{activeAdapter.Name}: {activeAdapter.NetworkInterfaceType}");
        }
        return CheckChannelInterference();
    }

    /// <summary>
    /// The ungated Wi-Fi channel check, for the "Wi-Fi keeps dropping" card where Wi-Fi is the
    /// whole subject - gating on the *active* adapter there would wrongly say "doesn't apply"
    /// for someone whose PC is on Ethernet but whose Wi-Fi still drops. Says so plainly when
    /// Wi-Fi isn't connected instead of the old misleading "couldn't determine your channel".
    /// </summary>
    public static CheckResult CheckChannelInterference()
    {
        const string techCommand = "netsh wlan show interfaces; netsh wlan show networks mode=bssid";
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (NoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi adapter found on this system - nothing to check here.", techCommand, "No wireless interface present.");

        var info = ParseInterfaceInfo(interfaceOutput);
        if (!info.TryGetValue("State", out var state) || !state.Equals("connected", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Warn, "Not connected to Wi-Fi right now, so there's no channel to check for interference.", techCommand, $"State = {state ?? "(not reported)"}");

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

    private static (int Ac, int Dc)? ReadWirelessPowerIndexes(out string raw)
    {
        raw = RunTool("powercfg", $"/query SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting}").Output;
        var ac = AcIndex.Match(raw);
        var dc = DcIndex.Match(raw);
        if (!ac.Success || !dc.Success) return null;
        return (Convert.ToInt32(ac.Groups[1].Value, 16), Convert.ToInt32(dc.Groups[1].Value, 16));
    }

    private static string PowerModeName(int index) =>
        index >= 0 && index < PowerModeNames.Length ? PowerModeNames[index] : $"mode {index}";

    public static CheckResult CheckPowerSaving()
    {
        var techCommand = $"powercfg /query SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting}";
        var indexes = ReadWirelessPowerIndexes(out var raw);
        if (string.IsNullOrWhiteSpace(raw))
            return new CheckResult(CheckStatus.Warn, "Couldn't read your Wi-Fi power-saving setting.", techCommand, "powercfg returned no output.");
        if (indexes is null)
            return new CheckResult(CheckStatus.Pass, "Your power plan has no Wi-Fi power-saving setting - nothing to change here.", techCommand, "No AC/DC power setting index in powercfg output.");

        var (ac, dc) = indexes.Value;
        var techDetail = $"AC index = {ac} ({PowerModeName(ac)}), DC index = {dc} ({PowerModeName(dc)})";
        return Math.Max(ac, dc) >= 2
            ? new CheckResult(CheckStatus.Warn, $"Windows lets your Wi-Fi adapter power down to save energy (plugged in: {PowerModeName(ac)}, on battery: {PowerModeName(dc)}) - that can cause drops.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Pass, "Windows isn't aggressively powering down your Wi-Fi adapter.", techCommand, techDetail);
    }

    /// <summary>
    /// Sets the active power plan's Wi-Fi power-saving mode to Maximum Performance (plugged in and
    /// on battery), remembering the previous values for <see cref="RestoreWirelessPowerSavingAsync"/>.
    /// Only changes this one documented power-plan value - no registry editing, no adapter changes.
    /// </summary>
    public static async Task<bool> DisableWirelessPowerSavingAsync() => await Task.Run(() =>
    {
        _powerIndexesBeforeFix = ReadWirelessPowerIndexes(out _);
        var a = RunTool("powercfg", $"/setacvalueindex SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting} 0").ExitCode;
        var d = RunTool("powercfg", $"/setdcvalueindex SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting} 0").ExitCode;
        var s = RunTool("powercfg", "/setactive SCHEME_CURRENT").ExitCode;
        return a == 0 && d == 0 && s == 0;
    });

    public static async Task<bool> RestoreWirelessPowerSavingAsync() => await Task.Run(() =>
    {
        if (_powerIndexesBeforeFix is not { } before) return false;
        var a = RunTool("powercfg", $"/setacvalueindex SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting} {before.Ac}").ExitCode;
        var d = RunTool("powercfg", $"/setdcvalueindex SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting} {before.Dc}").ExitCode;
        var s = RunTool("powercfg", "/setactive SCHEME_CURRENT").ExitCode;
        return a == 0 && d == 0 && s == 0;
    });

    /// <summary>WQL treats backslash and single quote as escape characters inside string literals.
    /// PNP device IDs contain backslashes (PCI\VEN_...), which made the driver query invalid and
    /// this check silently fail before.</summary>
    private static string EscapeWql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    public static CheckResult CheckDriverStatus()
    {
        const string techCommand = "Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceID -eq <Wi-Fi adapter PNPDeviceID>";
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (NoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi adapter found on this system - nothing to check here.", "netsh wlan show interfaces", "No wireless interface present.");

        var info = ParseInterfaceInfo(interfaceOutput);
        if (!info.TryGetValue("Name", out var adapterName))
            return new CheckResult(CheckStatus.Warn, "Couldn't identify the Wi-Fi adapter to check its driver.", "netsh wlan show interfaces", "No Name field in netsh output.");

        try
        {
            using var adapterSearcher = new ManagementObjectSearcher(
                $"SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE NetConnectionID = '{EscapeWql(adapterName)}'");
            var pnpDeviceId = adapterSearcher.Get().Cast<ManagementObject>().FirstOrDefault()?["PNPDeviceID"] as string;
            if (pnpDeviceId is null)
                return new CheckResult(CheckStatus.Warn, "Couldn't look up the Wi-Fi adapter's driver.", techCommand, $"No Win32_NetworkAdapter match for NetConnectionID '{adapterName}'.");

            using var driverSearcher = new ManagementObjectSearcher(
                $"SELECT DriverDate, DriverVersion, DriverProviderName FROM Win32_PnPSignedDriver WHERE DeviceID = '{EscapeWql(pnpDeviceId)}'");
            var driver = driverSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            var driverDateRaw = driver?["DriverDate"] as string;
            if (driverDateRaw is null)
                return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date.", techCommand, $"{adapterName} ({pnpDeviceId}): no DriverDate returned.");

            var driverDate = ManagementDateTimeConverter.ToDateTime(driverDateRaw);
            var ageDays = (DateTime.Now - driverDate).TotalDays;
            var techDetail = $"{adapterName}: provider = {driver?["DriverProviderName"]}, version = {driver?["DriverVersion"]}, date = {driverDate:yyyy-MM-dd} ({ageDays:N0} days old)";

            return ageDays < 730
                ? new CheckResult(CheckStatus.Pass, $"Driver is from {driverDate:MMMM yyyy} - reasonably current.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Warn, $"Driver hasn't been updated since {driverDate:MMMM yyyy} - a newer one may fix stability issues.", techCommand, techDetail);
        }
        catch (ManagementException ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date on this system.", techCommand, $"ManagementException: {ex.Message}");
        }
    }
}
