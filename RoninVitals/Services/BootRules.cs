using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

public enum DirtyState { Clean, Dirty, Unknown }

/// <summary>Start-up, shutdown and disk-flag judgments, kept apart from the code that reads the machine.</summary>
public static partial class BootRules
{
    public const double SlowBootSeconds = 60;
    public const double VerySlowBootSeconds = 120;
    public const int ShutdownWindowDays = 30;
    public const int RepeatedShutdownCount = 3;

    public static CheckResult JudgeBootTimes(IReadOnlyList<double> bootSeconds)
    {
        if (bootSeconds.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Windows isn't recording start-up times on this PC, so there's no number to judge. Programs that launch at start-up are the usual cause of a slow start.");

        var average = bootSeconds.Average();
        var text = $"Windows took {average:0} seconds to start on average over your last {bootSeconds.Count} {CheckText.Plural(bootSeconds.Count, "start-up", "start-ups")}.";
        if (average >= VerySlowBootSeconds) return new CheckResult(CheckStatus.Bad, $"{text} That's very slow.");
        if (average >= SlowBootSeconds) return new CheckResult(CheckStatus.Warn, $"{text} That's slower than it should be.");
        return new CheckResult(CheckStatus.Pass, $"{text} That's a healthy time.");
    }

    public static CheckResult JudgeShutdowns(int unexpectedShutdowns, IReadOnlyList<string> blueScreenCodes)
    {
        if (blueScreenCodes.Count > 0)
        {
            var codes = CheckText.Examples(blueScreenCodes, 2);
            return new CheckResult(CheckStatus.Bad, $"Windows crashed with a blue screen {blueScreenCodes.Count} {CheckText.Plural(blueScreenCodes.Count, "time", "times")} in the last {ShutdownWindowDays} days (stop code {codes}).");
        }

        if (unexpectedShutdowns == 0)
            return new CheckResult(CheckStatus.Pass, $"Windows has shut down cleanly every time in the last {ShutdownWindowDays} days.");

        var text = $"The PC shut down unexpectedly {unexpectedShutdowns} {CheckText.Plural(unexpectedShutdowns, "time", "times")} in the last {ShutdownWindowDays} days (a power loss, a hard reset, or a freeze).";
        return new CheckResult(unexpectedShutdowns >= RepeatedShutdownCount ? CheckStatus.Bad : CheckStatus.Warn, text);
    }

    /// <summary>The stop code Windows records for a blue screen, such as 0x0000009f, from the event's description.</summary>
    public static string? ExtractStopCode(string eventText)
    {
        var match = StopCodePattern().Match(eventText);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// <summary>Whether Windows' recovery environment (what offers Startup Repair when boot fails) is enabled, read from ReAgent.xml.</summary>
    public static CheckResult JudgeRecoveryEnvironment(string reagentXml)
    {
        var match = RecoveryStatePattern().Match(reagentXml);
        if (!match.Success)
            return new CheckResult(CheckStatus.Warn, "Couldn't tell whether Windows' recovery tools are set up.");

        return match.Groups[1].Value == "1"
            ? new CheckResult(CheckStatus.Pass, "Windows' recovery tools (Startup Repair) are set up and ready if boot ever fails.")
            : new CheckResult(CheckStatus.Warn, "Windows' recovery tools are turned off, so Windows can't offer Startup Repair if it fails to start.");
    }

    public static DirtyState InterpretDirtyQuery(string fsutilOutput)
    {
        if (fsutilOutput.Contains("NOT Dirty", StringComparison.OrdinalIgnoreCase)) return DirtyState.Clean;
        if (fsutilOutput.Contains("is Dirty", StringComparison.OrdinalIgnoreCase)) return DirtyState.Dirty;
        return DirtyState.Unknown;
    }

    public static CheckResult JudgeDirtyDrives(IReadOnlyList<(string Drive, DirtyState State)> drives)
    {
        var dirty = drives.Where(drive => drive.State == DirtyState.Dirty).Select(drive => drive.Drive).ToList();
        if (dirty.Count > 0)
            return new CheckResult(CheckStatus.Bad, $"Windows has flagged {CheckText.Examples(dirty)} as needing a disk check, which means it wasn't shut down cleanly or the drive has errors.");

        if (drives.Count == 0 || drives.All(drive => drive.State == DirtyState.Unknown))
            return new CheckResult(CheckStatus.Warn, "Couldn't read the disk-check flag on your drives.");

        return new CheckResult(CheckStatus.Pass, "No drive is flagged as needing a disk check.");
    }

    [GeneratedRegex(@"0x[0-9a-fA-F]{8}")]
    private static partial Regex StopCodePattern();

    [GeneratedRegex(@"<InstallState\s+state=""(\d)""")]
    private static partial Regex RecoveryStatePattern();
}
