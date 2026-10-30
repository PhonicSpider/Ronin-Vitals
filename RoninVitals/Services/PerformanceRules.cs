using RoninVitals.Models;

namespace RoninVitals.Services;

public static class PerformanceRules
{
    public const int RestartSuggestedDays = 7;
    public const int RestartOverdueDays = 21;

    /// <summary>Windows' "Fast startup" keeps the session alive through a normal shutdown, so uptime can be long even if the PC is switched off nightly.</summary>
    public static CheckResult JudgeTimeSinceRestart(TimeSpan uptime)
    {
        var days = (int)uptime.TotalDays;
        var text = $"Your PC hasn't had a full restart in {days} {CheckText.Plural(days, "day", "days")}.";

        if (days >= RestartOverdueDays) return new CheckResult(CheckStatus.Bad, $"{text} A restart is overdue and often makes a slow PC fast again.");
        if (days >= RestartSuggestedDays) return new CheckResult(CheckStatus.Warn, $"{text} A restart would clear memory that programs haven't given back.");
        return new CheckResult(CheckStatus.Pass, days == 0 ? "The PC was restarted today." : $"The PC was restarted {days} {CheckText.Plural(days, "day", "days")} ago.");
    }
}
