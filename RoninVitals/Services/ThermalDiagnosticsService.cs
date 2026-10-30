using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only heat and power checks. Windows only exposes motherboard thermal zones, never the CPU core temperature (that needs a kernel driver).</summary>
public static partial class ThermalDiagnosticsService
{
    private const string ThermalZoneQuery = "SELECT Name, HighPrecisionTemperature, PercentPassiveLimit, ThrottleReasons FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation";
    private const long MillisecondsPerDay = 86_400_000;

    private sealed record ThermalZone(string Name, double Celsius, uint PassiveLimitPercent, uint ThrottleReasons);

    private static List<ThermalZone> ReadThermalZones()
    {
        using var searcher = new ManagementObjectSearcher(ThermalZoneQuery);
        var zones = new List<ThermalZone>();
        foreach (ManagementBaseObject zone in searcher.Get())
        {
            using (zone)
            {
                zones.Add(new ThermalZone(
                    Name: zone["Name"]?.ToString() ?? "thermal zone",
                    Celsius: ThermalRules.TenthsOfKelvinToCelsius(Convert.ToDouble(zone["HighPrecisionTemperature"] ?? 0)),
                    PassiveLimitPercent: Convert.ToUInt32(zone["PercentPassiveLimit"] ?? 100u),
                    ThrottleReasons: Convert.ToUInt32(zone["ThrottleReasons"] ?? 0u)));
            }
        }
        return zones;
    }

    public static CheckResult CheckTemperature()
    {
        const string techCommand = "Get-CimInstance Win32_PerfFormattedData_Counters_ThermalZoneInformation | Select Name,HighPrecisionTemperature";
        try
        {
            var zones = ReadThermalZones().Where(zone => zone.Celsius > -50 && zone.Celsius < 150).ToList();
            if (zones.Count == 0)
                return new CheckResult(CheckStatus.Warn, "This PC doesn't share any temperature readings with Windows. A tool such as HWiNFO can read the CPU temperature directly.", techCommand, "No thermal zones returned.");

            var hottest = zones.MaxBy(zone => zone.Celsius)!;
            var detail = string.Join("; ", zones.Select(zone => $"{zone.Name}: {zone.Celsius:0.0} °C"));
            return ThermalRules.JudgeTemperature(hottest.Celsius) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read this PC's temperature sensors.", techCommand, CheckText.DescribeException(exception));
        }
    }

    /// <summary>Combines Windows' live throttle flags with its record of the firmware slowing the CPU (Kernel-Processor-Power event 37).</summary>
    public static CheckResult CheckThermalThrottling()
    {
        const string techCommand = "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-Kernel-Processor-Power';Id=37}";
        var query = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Processor-Power'] and EventID=37 and TimeCreated[timediff(@SystemTime) <= {ThermalRules.RecentThrottleDays * MillisecondsPerDay}]]]";
        try
        {
            var zones = ReadThermalZones();
            var throttlingNow = zones.Any(zone => zone.PassiveLimitPercent < 100 || zone.ThrottleReasons != 0);
            var limitEvents = EventLogDiagnosticsService.ReadEvents("System", query).Count;

            var zoneDetail = zones.Count == 0
                ? "No thermal zones reported"
                : string.Join(", ", zones.Select(zone => $"{zone.Name} passive limit {zone.PassiveLimitPercent}%, throttle reasons {zone.ThrottleReasons}"));
            var detail = $"{zoneDetail}. Firmware speed-limit events (ID 37) in {ThermalRules.RecentThrottleDays} days: {limitEvents}.";
            return ThermalRules.JudgeThrottling(throttlingNow, limitEvents) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is ManagementException or EventLogException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check whether the CPU is being slowed down.", techCommand, CheckText.DescribeException(exception));
        }
    }

    public static CheckResult CheckFans()
    {
        const string techCommand = "Get-CimInstance Win32_Fan | Select Name,Status,ActiveCooling";
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Status FROM Win32_Fan");
            var statuses = new List<string>();
            foreach (ManagementBaseObject fan in searcher.Get())
            {
                using (fan) statuses.Add(fan["Status"]?.ToString() ?? "Unknown");
            }

            var problems = statuses.Count(status => !status.Equals("OK", StringComparison.OrdinalIgnoreCase) && !status.Equals("Unknown", StringComparison.OrdinalIgnoreCase));
            var detail = statuses.Count == 0 ? "Win32_Fan returned no devices." : $"Statuses: {string.Join(", ", statuses)}.";
            return ThermalRules.JudgeFans(statuses.Count, problems) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the fan list from Windows.", techCommand, CheckText.DescribeException(exception));
        }
    }

    public static CheckResult CheckPowerPlanForHeat() => CheckPowerPlan(ThermalRules.JudgePlanForHeat);

    public static CheckResult CheckPowerPlanForSpeed() => CheckPowerPlan(ThermalRules.JudgePlanForSpeed);

    private static CheckResult CheckPowerPlan(Func<PowerPlan, CheckResult> judge)
    {
        const string techCommand = "powercfg /getactivescheme";
        var run = ProcessRunner.Run("powercfg.exe", "/getactivescheme", TimeSpan.FromSeconds(10));
        var match = PlanGuidPattern().Match(run.Output);
        if (!run.Succeeded || !match.Success)
            return new CheckResult(CheckStatus.Warn, "Couldn't read your power plan.", techCommand, string.IsNullOrEmpty(run.Error) ? run.Output : run.Error);

        var plan = ThermalRules.IdentifyPowerPlan(match.Value);
        return judge(plan) with { TechCommand = techCommand, TechDetail = run.Output };
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PlanGuidPattern();
}
