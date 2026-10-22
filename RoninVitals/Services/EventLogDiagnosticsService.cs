using System.Diagnostics.Eventing.Reader;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real checks backed by the Windows Event Log (System/Application logs). Read-only -
/// EventLogReader only queries existing entries, never writes, clears, or configures logging.
/// </summary>
public static class EventLogDiagnosticsService
{
    private static int CountEvents(string logName, string xpathQuery)
    {
        var query = new EventLogQuery(logName, PathType.LogName, xpathQuery);
        using var reader = new EventLogReader(query);

        var count = 0;
        EventRecord? record;
        while ((record = reader.ReadEvent()) is not null)
        {
            count++;
            record.Dispose();
        }
        return count;
    }

    private static CheckResult SafeCount(string logName, string xpathQuery, Func<int, CheckResult> toResult, string failureMessage)
    {
        try
        {
            return toResult(CountEvents(logName, xpathQuery));
        }
        catch (EventLogException)
        {
            return new CheckResult(CheckStatus.Warn, failureMessage);
        }
        catch (UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, $"{failureMessage} (access denied).");
        }
    }

    public static CheckResult CheckRecentCrashes() => SafeCount(
        "Application",
        "*[System[Provider[@Name='Application Error'] and TimeCreated[timediff(@SystemTime) <= 86400000]]]",
        count => count switch
        {
            0 => new CheckResult(CheckStatus.Pass, "No application crashes in the last 24 hours."),
            <= 2 => new CheckResult(CheckStatus.Warn, $"{count} application crash{(count == 1 ? "" : "es")} in the last 24 hours."),
            _ => new CheckResult(CheckStatus.Bad, $"{count} application crashes in the last 24 hours - one program may be unstable."),
        },
        "Couldn't read the Application event log.");

    public static CheckResult CheckRecentSystemErrors() => SafeCount(
        "System",
        "*[System[(Level=2) and TimeCreated[timediff(@SystemTime) <= 86400000]]]",
        count => count switch
        {
            0 => new CheckResult(CheckStatus.Pass, "No system errors in the last 24 hours."),
            <= 5 => new CheckResult(CheckStatus.Warn, $"{count} system error{(count == 1 ? "" : "s")} logged in the last 24 hours."),
            _ => new CheckResult(CheckStatus.Bad, $"{count} system errors logged in the last 24 hours - worth a closer look."),
        },
        "Couldn't read the System event log.");

    public static CheckResult CheckDiskErrors() => SafeCount(
        "System",
        "*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs']) and (Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]",
        count => count switch
        {
            0 => new CheckResult(CheckStatus.Pass, "No disk read/write errors in the last week."),
            <= 3 => new CheckResult(CheckStatus.Warn, $"{count} recoverable disk error{(count == 1 ? "" : "s")} found in the last week."),
            _ => new CheckResult(CheckStatus.Bad, $"{count} disk errors found in the last week - your drive may be failing."),
        },
        "Couldn't read the System event log for disk errors.");
}
