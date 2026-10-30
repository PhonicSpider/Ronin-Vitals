using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>All running instances of one program, added together (a browser alone can be dozens of processes).</summary>
public sealed record ProcessGroup(string Name, long WorkingSetBytes, int InstanceCount);

public static class ProcessRules
{
    public const double WarnShareOfMemory = 0.30;
    public const double BadShareOfMemory = 0.50;
    private const double BytesPerGigabyte = 1_073_741_824.0;

    public static IReadOnlyList<ProcessGroup> GroupByProgram(IEnumerable<(string Name, long WorkingSetBytes)> processes) =>
        processes
            .GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProcessGroup(group.Key, group.Sum(process => process.WorkingSetBytes), group.Count()))
            .OrderByDescending(group => group.WorkingSetBytes)
            .ToList();

    /// <summary>Judges the biggest memory user against the PC's total memory. <paramref name="ranked"/> must be biggest first.</summary>
    public static CheckResult JudgeMemoryUse(IReadOnlyList<ProcessGroup> ranked, long totalPhysicalBytes)
    {
        if (ranked.Count == 0 || totalPhysicalBytes <= 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't see which programs are using memory.");

        var top = ranked[0];
        var share = (double)top.WorkingSetBytes / totalPhysicalBytes;
        var gigabytes = top.WorkingSetBytes / BytesPerGigabyte;
        var copies = top.InstanceCount > 1 ? $" across {top.InstanceCount} processes" : "";
        var text = $"\"{top.Name}\" is using {gigabytes:0.0} GB of memory{copies} ({share:P0} of your RAM).";

        if (share >= BadShareOfMemory) return new CheckResult(CheckStatus.Bad, text);
        if (share >= WarnShareOfMemory) return new CheckResult(CheckStatus.Warn, text);
        return new CheckResult(CheckStatus.Pass, $"No single program is hogging memory. The biggest is \"{top.Name}\" at {gigabytes:0.0} GB{copies}.");
    }

    public static CheckResult JudgeNotResponding(IReadOnlyList<string> programNames) =>
        programNames.Count == 0
            ? new CheckResult(CheckStatus.Pass, "Every open program is responding.")
            : new CheckResult(CheckStatus.Warn, $"{CheckText.Examples(programNames)} {CheckText.Plural(programNames.Count, "has", "have")} stopped responding. Closing {CheckText.Plural(programNames.Count, "it", "them")} from Task Manager usually clears it.");
}
