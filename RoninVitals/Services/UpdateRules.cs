using System.ServiceProcess;
using RoninVitals.Models;

namespace RoninVitals.Services;

public sealed record UpdateFailure(string ErrorCode, string UpdateTitle);

public sealed record ServiceState(string Name, ServiceControllerStatus Status, ServiceStartMode StartMode);

/// <summary>Windows Update judgments kept apart from the code that reads the machine, so they can be tested directly.</summary>
public static class UpdateRules
{
    public const double SystemDriveWarnGigabytes = 20;
    public const double SystemDriveBadGigabytes = 10;

    private static readonly Dictionary<string, string> KnownErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0x80070002"] = "a file Windows needed was missing, often a damaged update download",
        ["0x80070003"] = "a folder Windows needed was missing, often a damaged update download",
        ["0x80070005"] = "access was denied, often by security software",
        ["0x80070057"] = "the update data was damaged or invalid",
        ["0x8007000D"] = "the update data was damaged",
        ["0x80070643"] = "the installer hit a fatal error",
        ["0x800705B4"] = "the update timed out",
        ["0x80073712"] = "a Windows component file is missing or damaged",
        ["0x800F081F"] = "the source files for the update could not be found",
        ["0x800F0922"] = "there isn't enough room on the system partition, or a VPN or security program interfered",
        ["0x8024001E"] = "the update service was stopped while working",
        ["0x80240034"] = "the update failed to download",
        ["0x8024401C"] = "the update servers could not be reached (network, proxy or DNS)",
        ["0x8024402C"] = "the update servers could not be reached (network, proxy or DNS)",
        ["0x80244022"] = "the update servers were too busy or unreachable",
    };

    /// <summary>Plain-language meaning of a Windows Update error code, or null when the code isn't a known one.</summary>
    public static string? DescribeErrorCode(string errorCode) =>
        KnownErrors.GetValueOrDefault(errorCode);

    /// <summary>Formats the number the event log stores (for example 2147942402) as the 0x80070002 form people search for.</summary>
    public static string ToHexCode(string rawValue) =>
        uint.TryParse(rawValue, out var number) ? $"0x{number:X8}" : rawValue;

    public static CheckResult SummarizeFailures(IReadOnlyList<UpdateFailure> failures)
    {
        if (failures.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows hasn't logged any failed updates in the last 30 days.");

        var latest = failures[0];
        var meaning = DescribeErrorCode(latest.ErrorCode);
        var because = meaning is null ? $"with error {latest.ErrorCode}" : $"because {meaning} (error {latest.ErrorCode})";
        var text = $"{failures.Count} update {CheckText.Plural(failures.Count, "failure", "failures")} in the last 30 days. The latest, \"{Shorten(latest.UpdateTitle)}\", failed {because}.";
        return new CheckResult(failures.Count >= 3 ? CheckStatus.Bad : CheckStatus.Warn, text);
    }

    public static CheckResult JudgeServices(IReadOnlyList<ServiceState> services)
    {
        var disabled = services.Where(service => service.StartMode == ServiceStartMode.Disabled).Select(service => service.Name).ToList();
        if (disabled.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"Windows Update can't work: {CheckText.Examples(disabled)} {CheckText.Plural(disabled.Count, "is", "are")} switched off.");

        // Update services are usually "Stopped" and start themselves on demand, so a stopped one is normal.
        return new CheckResult(CheckStatus.Pass, "The services Windows Update relies on are allowed to run.");
    }

    public static CheckResult JudgeSystemDriveSpace(string driveName, double freeGigabytes)
    {
        var text = $"{driveName} has {freeGigabytes:0.#} GB free; big Windows updates need about {SystemDriveBadGigabytes:0}-{SystemDriveWarnGigabytes:0} GB of room.";
        if (freeGigabytes < SystemDriveBadGigabytes) return new CheckResult(CheckStatus.Bad, $"Not enough room to update: {text}");
        if (freeGigabytes < SystemDriveWarnGigabytes) return new CheckResult(CheckStatus.Warn, $"Tight on room: {text}");
        return new CheckResult(CheckStatus.Pass, $"Plenty of room for updates: {text}");
    }

    public static CheckResult SummarizePendingUpdates(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows is up to date: no updates are waiting.");

        var text = $"{titles.Count} Windows {CheckText.Plural(titles.Count, "update is", "updates are")} waiting to install: {CheckText.Examples(titles.Select(Shorten), 2)}.";
        return new CheckResult(CheckStatus.Warn, text);
    }

    /// <param name="routineFileReplacements">Installers often leave a few files to swap at the next restart; that alone isn't worth a warning.</param>
    public static CheckResult SummarizePendingRestart(IReadOnlyList<string> reasons, bool routineFileReplacements = false)
    {
        if (reasons.Count > 0)
            return new CheckResult(CheckStatus.Warn, $"Windows is waiting for a restart to finish {CheckText.Examples(reasons, 3)}. Restarting often clears update and startup problems.");

        return routineFileReplacements
            ? new CheckResult(CheckStatus.Pass, "No update is waiting for a restart. A few routine file replacements will happen at the next one.")
            : new CheckResult(CheckStatus.Pass, "Nothing is waiting for a restart.");
    }

    private static string Shorten(string title) => title.Length > 70 ? title[..67] + "..." : title;
}
