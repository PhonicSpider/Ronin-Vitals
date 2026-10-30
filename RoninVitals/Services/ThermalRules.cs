using RoninVitals.Models;

namespace RoninVitals.Services;

public enum PowerPlan { PowerSaver, Balanced, HighPerformance, Ultimate, Other }

/// <summary>Heat, throttling and power-plan judgments, kept apart from the code that reads the sensors.</summary>
public static class ThermalRules
{
    public const double WarmCelsius = 70;
    public const double HotCelsius = 85;
    public const int RecentThrottleDays = 7;

    private static readonly Dictionary<string, PowerPlan> KnownPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a1841308-3541-4fab-bc81-f71556f20b4a"] = PowerPlan.PowerSaver,
        ["381b4222-f694-41f0-9685-ff5bb260df2e"] = PowerPlan.Balanced,
        ["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = PowerPlan.HighPerformance,
        ["e9a42b02-d5df-448d-aa00-03f14749eb61"] = PowerPlan.Ultimate,
    };

    public static PowerPlan IdentifyPowerPlan(string planGuid) =>
        KnownPlans.GetValueOrDefault(planGuid.Trim(), PowerPlan.Other);

    /// <summary>Kelvin to Celsius. Windows reports thermal zones in tenths of a kelvin.</summary>
    public static double TenthsOfKelvinToCelsius(double tenthsOfKelvin) => tenthsOfKelvin / 10.0 - 273.15;

    public static CheckResult JudgeTemperature(double hottestCelsius)
    {
        const string caveat = " This is the hottest sensor Windows can see (usually on the motherboard), not the CPU core itself.";
        var reading = $"{hottestCelsius:0} °C";
        if (hottestCelsius >= HotCelsius) return new CheckResult(CheckStatus.Bad, $"Running very hot: {reading}.{caveat}");
        if (hottestCelsius >= WarmCelsius) return new CheckResult(CheckStatus.Warn, $"Running warm: {reading}.{caveat}");
        return new CheckResult(CheckStatus.Pass, $"Temperature looks fine: {reading}.{caveat}");
    }

    public static CheckResult JudgeThrottling(bool throttlingNow, int firmwareLimitEvents)
    {
        if (throttlingNow)
            return new CheckResult(CheckStatus.Bad, "The CPU is being slowed down right now to keep it cool or within its power limit.");

        if (firmwareLimitEvents > 0)
            return new CheckResult(CheckStatus.Warn,
                $"Windows logged {firmwareLimitEvents} {CheckText.Plural(firmwareLimitEvents, "time", "times")} in the last {RecentThrottleDays} days that the PC's firmware slowed the CPU down (usually heat or a power limit).");

        return new CheckResult(CheckStatus.Pass, $"The CPU isn't being slowed down, and Windows hasn't logged any slowdowns in the last {RecentThrottleDays} days.");
    }

    public static CheckResult JudgeFans(int coolingDevices, int devicesReportingProblems)
    {
        const string limits = " Windows can't read fan speed or tell whether a fan is clogged with dust, so listen for loud or grinding fans.";
        if (devicesReportingProblems > 0)
            return new CheckResult(CheckStatus.Warn, $"{devicesReportingProblems} of {coolingDevices} cooling {CheckText.Plural(coolingDevices, "device reports", "devices report")} a problem.");

        return coolingDevices == 0
            ? new CheckResult(CheckStatus.Pass, "Windows doesn't list any fans on this PC (the motherboard usually controls them directly)." + limits)
            : new CheckResult(CheckStatus.Pass, $"Windows lists {coolingDevices} cooling {CheckText.Plural(coolingDevices, "device", "devices")} and none report a problem." + limits);
    }

    /// <summary>For "feels slow": Power saver holds the CPU back on purpose.</summary>
    public static CheckResult JudgePlanForSpeed(PowerPlan plan) => plan switch
    {
        PowerPlan.PowerSaver => new CheckResult(CheckStatus.Warn, "Your power plan is Power saver, which deliberately slows the PC down to save energy."),
        PowerPlan.Other => new CheckResult(CheckStatus.Pass, "You're using a custom power plan; Vitals can't tell how it treats performance."),
        _ => new CheckResult(CheckStatus.Pass, $"Your power plan ({DescribePlan(plan)}) doesn't hold back performance."),
    };

    /// <summary>For "hot or loud": the high-performance plans keep the CPU fast and warm even when idle.</summary>
    public static CheckResult JudgePlanForHeat(PowerPlan plan) => plan switch
    {
        PowerPlan.HighPerformance or PowerPlan.Ultimate => new CheckResult(CheckStatus.Warn, $"Your power plan is {DescribePlan(plan)}, which keeps the CPU running fast and warm even when you're not busy."),
        PowerPlan.Other => new CheckResult(CheckStatus.Pass, "You're using a custom power plan; Vitals can't tell how it treats heat."),
        _ => new CheckResult(CheckStatus.Pass, $"Your power plan ({DescribePlan(plan)}) lets the CPU cool down when it's idle."),
    };

    public static string DescribePlan(PowerPlan plan) => plan switch
    {
        PowerPlan.PowerSaver => "Power saver",
        PowerPlan.Balanced => "Balanced",
        PowerPlan.HighPerformance => "High performance",
        PowerPlan.Ultimate => "Ultimate performance",
        _ => "custom",
    };
}
