using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>One logged error, reduced to who reported it and which event it was.</summary>
public sealed record LoggedError(string Source, int EventId);

public static class ErrorRules
{
    public const int WarnUpToCount = 10;

    /// <summary>Microsoft documents these as harmless noise on healthy PCs (DistributedCOM permission and registration timeouts).</summary>
    private static readonly HashSet<(string Source, int EventId)> RoutineErrors =
    [
        ("Microsoft-Windows-DistributedCOM", 10016),
        ("Microsoft-Windows-DistributedCOM", 10010),
    ];

    private static readonly Dictionary<int, string> ServiceControlManagerMeanings = new()
    {
        [7000] = "a service failed to start",
        [7001] = "a service couldn't start because another one it needs failed",
        [7009] = "a service took too long to start",
        [7011] = "a service stopped answering",
        [7023] = "a service stopped with an error",
        [7031] = "a service crashed",
        [7034] = "a service crashed",
    };

    public static bool IsRoutine(LoggedError error) => RoutineErrors.Contains((error.Source, error.EventId));

    public static CheckResult SummarizeSystemErrors(IReadOnlyList<LoggedError> errors)
    {
        var meaningful = errors.Where(error => !IsRoutine(error)).ToList();
        var setAside = errors.Count - meaningful.Count;
        var setAsideNote = setAside > 0 ? $" ({setAside} routine {CheckText.Plural(setAside, "message was", "messages were")} set aside)" : "";

        if (meaningful.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"No system errors worth worrying about in the last 24 hours{setAsideNote}.");

        var top = meaningful.GroupBy(error => (error.Source, error.EventId)).OrderByDescending(group => group.Count()).First();
        var meaning = top.Key.Source == "Service Control Manager" && ServiceControlManagerMeanings.TryGetValue(top.Key.EventId, out var description) ? $": {description}" : "";
        var summary = $"{meaningful.Count} system {CheckText.Plural(meaningful.Count, "error", "errors")} in the last 24 hours, most from {top.Key.Source} (event {top.Key.EventId}{meaning}){setAsideNote}.";

        return new CheckResult(meaningful.Count <= WarnUpToCount ? CheckStatus.Warn : CheckStatus.Bad, summary);
    }

    public static string DescribeSources(IReadOnlyList<LoggedError> errors) =>
        errors.Count == 0
            ? "No error events (level 2) in the last 24 hours."
            : string.Join("; ", errors
                .GroupBy(error => (error.Source, error.EventId))
                .OrderByDescending(group => group.Count())
                .Take(5)
                .Select(group => $"{group.Key.Source} {group.Key.EventId} x{group.Count()}{(RoutineErrors.Contains(group.Key) ? " (routine)" : "")}"));
}
