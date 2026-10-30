using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only checks about how Windows starts and shuts down.</summary>
public static class BootDiagnosticsService
{
    private const string BootPerformanceLog = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private const int BootsToAverage = 5;
    private const long MillisecondsPerDay = 86_400_000;
    private static readonly TimeSpan DiskFlagTimeout = TimeSpan.FromSeconds(15);

    public static CheckResult CheckStartupTime()
    {
        const string techCommand = "Get-WinEvent -LogName 'Microsoft-Windows-Diagnostics-Performance/Operational' | Where-Object Id -eq 100";
        try
        {
            var boots = EventLogDiagnosticsService.ReadEvents(BootPerformanceLog, "*[System[(EventID=100)]]", BootsToAverage, includeNamedData: true);
            var seconds = boots
                .Select(boot => boot.NamedData.TryGetValue("BootTime", out var milliseconds) && double.TryParse(milliseconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value / 1000.0 : (double?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            var detail = seconds.Count == 0 ? "No boot-time (event 100) records found." : $"Recent boot times: {string.Join(", ", seconds.Select(value => $"{value:0}s"))}.";
            return BootRules.JudgeBootTimes(seconds) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (EventLogNotFoundException)
        {
            return BootRules.JudgeBootTimes([]) with { TechCommand = techCommand, TechDetail = $"Log '{BootPerformanceLog}' not found." };
        }
        catch (Exception exception) when (exception is EventLogException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read Windows' start-up timing log.", techCommand, CheckText.DescribeException(exception));
        }
    }

    /// <summary>Counts unexpected shutdowns (Kernel-Power 41) and blue screens (BugCheck 1001) from the last 30 days.</summary>
    public static CheckResult CheckUnexpectedShutdowns()
    {
        const string techCommand = "Get-WinEvent -FilterHashtable @{LogName='System';Id=41,1001}";
        var window = $"TimeCreated[timediff(@SystemTime) <= {BootRules.ShutdownWindowDays * MillisecondsPerDay}]";
        try
        {
            var unexpected = EventLogDiagnosticsService.ReadEvents("System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and {window}]]").Count;
            var blueScreens = EventLogDiagnosticsService.ReadEvents("System", $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and {window}]]");
            var stopCodes = blueScreens
                .Select(screen => (screen.Fields.Count > 0 ? BootRules.ExtractStopCode(screen.Fields[0]) : null) ?? "unknown")
                .ToList();

            var detail = $"Unexpected shutdowns (event 41): {unexpected}. Blue screens (event 1001): {blueScreens.Count}{(stopCodes.Count > 0 ? $", codes {string.Join(", ", stopCodes.Distinct())}" : "")}.";
            return BootRules.JudgeShutdowns(unexpected, stopCodes) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is EventLogException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the shutdown history.", techCommand, CheckText.DescribeException(exception));
        }
    }

    public static CheckResult CheckRecoveryEnvironment()
    {
        const string techCommand = "reagentc /info";
        var configPath = Path.Combine(Environment.SystemDirectory, "Recovery", "ReAgent.xml");
        try
        {
            return BootRules.JudgeRecoveryEnvironment(File.ReadAllText(configPath)) with { TechCommand = techCommand, TechDetail = $"Read {configPath}." };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read Windows' recovery settings.", techCommand, CheckText.DescribeException(exception));
        }
    }

    /// <summary>Asks NTFS whether each drive carries the "dirty" flag Windows sets when a disk check is needed. Needs administrator rights.</summary>
    public static CheckResult CheckDiskFlags()
    {
        const string techCommand = "fsutil dirty query C:";
        if (!ElevationService.IsElevated)
            return new CheckResult(CheckStatus.Warn, "Reading the disk-check flag needs administrator rights.", techCommand, "The app isn't running as administrator.");

        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => (Drive: drive.Name.TrimEnd('\\'), State: QueryDirtyFlag(drive.Name.TrimEnd('\\'))))
            .ToList();

        var detail = string.Join("; ", drives.Select(drive => $"{drive.Drive} {drive.State}"));
        return BootRules.JudgeDirtyDrives(drives) with { TechCommand = techCommand, TechDetail = detail };
    }

    private static DirtyState QueryDirtyFlag(string driveWithoutSlash)
    {
        var run = ProcessRunner.Run("fsutil.exe", ["dirty", "query", driveWithoutSlash], DiskFlagTimeout);
        return BootRules.InterpretDirtyQuery(run.Output);
    }
}
