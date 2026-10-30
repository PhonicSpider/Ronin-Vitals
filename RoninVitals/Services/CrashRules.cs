using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>One Application Error (crash) or Application Hang (freeze) from the event log.</summary>
public sealed record CrashEvent(string Program, string FaultingModule, bool WasFreeze);

/// <summary>Turns a week of crash events into one plain-language result. Pure, so it can be tested without an event log.</summary>
public static class CrashRules
{
    public const int WindowDays = 7;
    public const int RepeatedCrashCount = 3;

    public static CheckResult Summarize(IReadOnlyList<CrashEvent> events)
    {
        if (events.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"No programs crashed or froze in the last {WindowDays} days.");

        var byProgram = events
            .GroupBy(crash => crash.Program, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var worst = byProgram[0];
        var worstCount = worst.Count();
        var verb = worst.All(crash => crash.WasFreeze) ? "froze" : worst.Any(crash => crash.WasFreeze) ? "crashed or froze" : "crashed";
        var others = byProgram.Count - 1;
        var othersText = others == 0 ? "" : $" {others} other {CheckText.Plural(others, "program", "programs")} also had problems.";
        var cause = DescribeLikelyCause(worst.Select(crash => crash.FaultingModule));

        var text = $"{worst.Key} {verb} {worstCount} {CheckText.Plural(worstCount, "time", "times")} in the last {WindowDays} days.{cause}{othersText}";
        var status = worstCount >= RepeatedCrashCount ? CheckStatus.Bad : CheckStatus.Warn;
        return new CheckResult(status, text);
    }

    /// <summary>One line per program, most crashes first, with the module that most often failed.</summary>
    public static string DescribeEvents(IReadOnlyList<CrashEvent> events) =>
        events.Count == 0
            ? $"No Application Error (1000) or Application Hang (1002) events in the last {WindowDays} days."
            : string.Join("; ", events
                .GroupBy(crash => crash.Program, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(group => $"{group.Key} x{group.Count()} (module {MostCommon(group.Select(crash => crash.FaultingModule))})"));

    /// <summary>A graphics driver file in the crash is worth naming: it points at the driver, not the program.</summary>
    internal static string DescribeLikelyCause(IEnumerable<string> faultingModules)
    {
        var module = MostCommon(faultingModules).ToLowerInvariant();
        string[] graphicsPrefixes = ["nvlddmkm", "nvwgf2um", "nvd3dum", "nvoglv", "atidxx", "atiumd", "amdxx", "amdvlk", "igd", "ig9icd", "igxel", "d3d11", "d3d12", "dxgi"];
        return graphicsPrefixes.Any(module.StartsWith)
            ? " The crash points at a graphics driver file, so updating the graphics driver may help."
            : "";
    }

    private static string MostCommon(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
              .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
              .OrderByDescending(group => group.Count())
              .Select(group => group.Key)
              .FirstOrDefault() ?? "unknown";
}
