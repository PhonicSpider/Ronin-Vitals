using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Wi-Fi checks. Signal, channel and nearby networks come from <c>netsh wlan</c> (no .NET API exposes them), the driver
/// date from WMI, and power saving from the documented powercfg setting. Checks are read-only; the one fix only changes
/// that power-plan setting and can be undone.
/// </summary>
public static class WifiDiagnosticsService
{
    private const int StrongSignalPercent = 70;
    private const int WeakSignalPercent = 40;
    private const int StaleDriverDays = 730;
    private const int MediumPowerSavingMode = 2;
    private const string NoWifiAdapterMessage = "No Wi-Fi adapter found on this system - nothing to check here.";

    // powercfg ids for "Wireless Adapter Settings" and its "Power Saving Mode" (0 = Maximum Performance ... 3 = Maximum Power Saving).
    private const string WirelessSubgroup = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string WirelessPowerSetting = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    private static readonly string[] PowerModeNames = ["Maximum Performance", "Low Power Saving", "Medium Power Saving", "Maximum Power Saving"];

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);
    private static readonly Regex ChannelLine = new(@"Channel\s*:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex PluggedInIndexLine = new(@"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    private static readonly Regex OnBatteryIndexLine = new(@"Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);

    private sealed record PowerSavingModes(int PluggedIn, int OnBattery);

    /// <summary>The modes in place just before the fix ran, so undo can put them back exactly.</summary>
    private static PowerSavingModes? _modesBeforeFix;

    // ── netsh wlan ──────────────────────────────────────────────────────────────────────────

    private static string RunNetsh(string arguments) => ProcessRunner.Run("netsh", arguments, ToolTimeout).Output;

    private static Dictionary<string, string> ParseKeyValueLines(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 1) continue;
            var key = line[..separatorIndex].Trim();
            if (key.Length == 0 || values.ContainsKey(key)) continue;
            values[key] = line[(separatorIndex + 1)..].Trim();
        }
        return values;
    }

    private static bool HasNoWirelessInterface(string output) =>
        output.Contains("no wireless interface", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnected(Dictionary<string, string> interfaceInfo, out string state)
    {
        state = interfaceInfo.GetValueOrDefault("State") ?? "(not reported)";
        return state.Equals("connected", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Raw link details for the technician line only (never the friendly text).</summary>
    private static string DescribeLink(Dictionary<string, string> interfaceInfo)
    {
        string[] fields = ["SSID", "Band", "Radio type", "Channel", "Receive rate (Mbps)", "Transmit rate (Mbps)"];
        var present = fields
            .Where(field => interfaceInfo.TryGetValue(field, out var value) && value.Length > 0)
            .Select(field => $"{field} = {interfaceInfo[field]}")
            .ToList();
        return present.Count == 0 ? string.Empty : ". " + string.Join(", ", present);
    }

    /// <summary>For checks that only make sense on Wi-Fi: a result explaining why, when the active connection is wired.</summary>
    private static CheckResult? ExplainWhenNotOnWifi(string subject)
    {
        var adapter = ActiveAdapter.Find();
        if (adapter is null || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return null;

        var connectionType = adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "a wired connection";
        return new CheckResult(CheckStatus.Pass, $"You're connected over {connectionType}, not Wi-Fi - {subject} doesn't apply here.",
            "Get-NetAdapter | Where-Object Status -eq Up", $"{adapter.Name}: {adapter.NetworkInterfaceType}");
    }

    // ── Signal and interference ─────────────────────────────────────────────────────────────

    public static CheckResult CheckSignalStrength()
    {
        const string techCommand = "netsh wlan show interfaces";
        var output = RunNetsh("wlan show interfaces");
        if (HasNoWirelessInterface(output))
            return new CheckResult(CheckStatus.Pass, NoWifiAdapterMessage, techCommand, "No wireless interface present.");

        var interfaceInfo = ParseKeyValueLines(output);
        if (!IsConnected(interfaceInfo, out var state))
            return new CheckResult(CheckStatus.Warn, "Not currently connected to a Wi-Fi network.", techCommand, $"State = {state}");

        if (!interfaceInfo.TryGetValue("Signal", out var signalText) || !int.TryParse(signalText.TrimEnd('%'), out var signalPercent))
            return new CheckResult(CheckStatus.Warn, "Couldn't read Wi-Fi signal strength.", techCommand, "No Signal field in netsh output.");

        var techDetail = $"Signal = {signalPercent}%{DescribeLink(interfaceInfo)}";
        return signalPercent switch
        {
            >= StrongSignalPercent => new CheckResult(CheckStatus.Pass, $"Signal is strong ({signalPercent}%).", techCommand, techDetail),
            >= WeakSignalPercent => new CheckResult(CheckStatus.Warn, $"Signal is weaker than expected for this room ({signalPercent}%).", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Signal is very weak ({signalPercent}%) - move closer to the router.", techCommand, techDetail),
        };
    }

    /// <summary>Signal strength for cards that are not about Wi-Fi: says "doesn't apply" on a wired connection instead of warning about Wi-Fi.</summary>
    public static CheckResult CheckSignalStrengthIfActive() =>
        ExplainWhenNotOnWifi("signal strength") ?? CheckSignalStrength();

    /// <summary>Channel interference for cards that are not about Wi-Fi: says "doesn't apply" on a wired connection.</summary>
    public static CheckResult CheckNetworkCongestion() =>
        ExplainWhenNotOnWifi("channel congestion") ?? CheckChannelInterference();

    /// <summary>Counts nearby networks sharing this Wi-Fi channel. Not gated on the active adapter: a PC on Ethernet can still have a Wi-Fi problem.</summary>
    public static CheckResult CheckChannelInterference()
    {
        const string techCommand = "netsh wlan show interfaces; netsh wlan show networks mode=bssid";
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (HasNoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, NoWifiAdapterMessage, techCommand, "No wireless interface present.");

        var interfaceInfo = ParseKeyValueLines(interfaceOutput);
        if (!IsConnected(interfaceInfo, out var state))
            return new CheckResult(CheckStatus.Warn, "Not connected to Wi-Fi right now, so there's no channel to check for interference.", techCommand, $"State = {state}");

        if (!interfaceInfo.TryGetValue("Channel", out var channelText) || !int.TryParse(channelText, out var ownChannel))
            return new CheckResult(CheckStatus.Warn, "Couldn't determine which Wi-Fi channel you're on.", techCommand, "No Channel field in netsh output.");

        var visibleChannels = ChannelLine.Matches(RunNetsh("wlan show networks mode=bssid"))
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToList();
        var networksOnOwnChannel = visibleChannels.Count(channel => channel == ownChannel);
        var sharingNetworks = networksOnOwnChannel - 1; // not counting your own
        var techDetail = $"Your channel: {ownChannel}. Networks seen on that channel (incl. yours): {networksOnOwnChannel}.";

        return sharingNetworks switch
        {
            <= 0 => new CheckResult(CheckStatus.Pass, "No other networks sharing your Wi-Fi channel nearby.", techCommand, techDetail),
            <= 2 => new CheckResult(CheckStatus.Pass, $"{sharingNetworks} nearby network{(sharingNetworks == 1 ? "" : "s")} sharing your channel - not enough to worry about.", techCommand, techDetail),
            _ => new CheckResult(CheckStatus.Warn, $"{sharingNetworks} nearby networks are sharing your Wi-Fi channel - that can cause interference.", techCommand, techDetail),
        };
    }

    // ── Power saving ────────────────────────────────────────────────────────────────────────

    private static PowerSavingModes? ReadPowerSavingModes(out string rawOutput)
    {
        rawOutput = ProcessRunner.Run("powercfg", $"/query SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting}", ToolTimeout).Output;
        var pluggedIn = PluggedInIndexLine.Match(rawOutput);
        var onBattery = OnBatteryIndexLine.Match(rawOutput);
        if (!pluggedIn.Success || !onBattery.Success) return null;
        return new PowerSavingModes(Convert.ToInt32(pluggedIn.Groups[1].Value, 16), Convert.ToInt32(onBattery.Groups[1].Value, 16));
    }

    private static string DescribePowerMode(int mode) =>
        mode >= 0 && mode < PowerModeNames.Length ? PowerModeNames[mode] : $"mode {mode}";

    private static bool SetPowerSavingModes(PowerSavingModes modes)
    {
        var settingPath = $"SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting}";
        return ProcessRunner.Run("powercfg", $"/setacvalueindex {settingPath} {modes.PluggedIn}", ToolTimeout).Succeeded
            && ProcessRunner.Run("powercfg", $"/setdcvalueindex {settingPath} {modes.OnBattery}", ToolTimeout).Succeeded
            && ProcessRunner.Run("powercfg", "/setactive SCHEME_CURRENT", ToolTimeout).Succeeded;
    }

    public static CheckResult CheckPowerSaving()
    {
        var techCommand = $"powercfg /query SCHEME_CURRENT {WirelessSubgroup} {WirelessPowerSetting}";
        var modes = ReadPowerSavingModes(out var rawOutput);
        if (string.IsNullOrWhiteSpace(rawOutput))
            return new CheckResult(CheckStatus.Warn, "Couldn't read your Wi-Fi power-saving setting.", techCommand, "powercfg returned no output.");
        if (modes is null)
            return new CheckResult(CheckStatus.Pass, "Your power plan has no Wi-Fi power-saving setting - nothing to change here.", techCommand, "No AC/DC power setting index in powercfg output.");

        var pluggedIn = DescribePowerMode(modes.PluggedIn);
        var onBattery = DescribePowerMode(modes.OnBattery);
        var techDetail = $"AC index = {modes.PluggedIn} ({pluggedIn}), DC index = {modes.OnBattery} ({onBattery})";
        return Math.Max(modes.PluggedIn, modes.OnBattery) >= MediumPowerSavingMode
            ? new CheckResult(CheckStatus.Warn, $"Windows lets your Wi-Fi adapter power down to save energy (plugged in: {pluggedIn}, on battery: {onBattery}) - that can cause drops.", techCommand, techDetail)
            : new CheckResult(CheckStatus.Pass, "Windows isn't aggressively powering down your Wi-Fi adapter.", techCommand, techDetail);
    }

    /// <summary>SafeAutoFix: sets Wi-Fi power saving to Maximum Performance on the active power plan, remembering the old values for undo.</summary>
    public static Task<bool> DisableWirelessPowerSavingAsync() => Task.Run(() =>
    {
        _modesBeforeFix = ReadPowerSavingModes(out _);
        return SetPowerSavingModes(new PowerSavingModes(PluggedIn: 0, OnBattery: 0));
    });

    public static Task<bool> RestoreWirelessPowerSavingAsync() => Task.Run(() =>
        _modesBeforeFix is { } previousModes && SetPowerSavingModes(previousModes));

    // ── Driver ──────────────────────────────────────────────────────────────────────────────

    /// <summary>WQL treats backslash and quote as escapes; PNP device ids contain backslashes (PCI\VEN_...).</summary>
    private static string EscapeWql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    public static CheckResult CheckDriverStatus()
    {
        const string techCommand = "Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceID -eq <Wi-Fi adapter PNPDeviceID>";
        const string netshCommand = "netsh wlan show interfaces";
        var interfaceOutput = RunNetsh("wlan show interfaces");
        if (HasNoWirelessInterface(interfaceOutput))
            return new CheckResult(CheckStatus.Pass, NoWifiAdapterMessage, netshCommand, "No wireless interface present.");

        if (!ParseKeyValueLines(interfaceOutput).TryGetValue("Name", out var adapterName))
            return new CheckResult(CheckStatus.Warn, "Couldn't identify the Wi-Fi adapter to check its driver.", netshCommand, "No Name field in netsh output.");

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
            if (driver?["DriverDate"] is not string driverDateText)
                return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date.", techCommand, $"{adapterName} ({pnpDeviceId}): no DriverDate returned.");

            var driverDate = ManagementDateTimeConverter.ToDateTime(driverDateText);
            var ageInDays = (DateTime.Now - driverDate).TotalDays;
            var techDetail = $"{adapterName}: provider = {driver["DriverProviderName"]}, version = {driver["DriverVersion"]}, date = {driverDate:yyyy-MM-dd} ({ageInDays:N0} days old)";

            return ageInDays < StaleDriverDays
                ? new CheckResult(CheckStatus.Pass, $"Driver is from {driverDate:MMMM yyyy} - reasonably current.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Warn, $"Driver hasn't been updated since {driverDate:MMMM yyyy} - a newer one may fix stability issues.", techCommand, techDetail);
        }
        catch (ManagementException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Wi-Fi driver's date on this system.", techCommand, $"ManagementException: {exception.Message}");
        }
    }
}
