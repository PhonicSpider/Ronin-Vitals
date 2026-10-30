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

    private static CheckResult SafeCount(string logName, string xpathQuery, Func<int, CheckResult> toResult, string failureMessage, string techCommand)
    {
        try
        {
            var count = CountEvents(logName, xpathQuery);
            return toResult(count) with { TechCommand = techCommand, TechDetail = $"{count} matching {CheckText.Plural(count, "event", "events")} in the {logName} log." };
        }
        catch (EventLogException exception)
        {
            return new CheckResult(CheckStatus.Warn, failureMessage, techCommand, CheckText.DescribeException(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return new CheckResult(CheckStatus.Warn, $"{failureMessage} (access denied).", techCommand, CheckText.DescribeException(exception));
        }
    }

    /// <summary>
    /// Direct evidence of Wi-Fi drops: Windows logs every disconnect (event 8003) and failed
    /// connection attempt (8002) to the WLAN AutoConfig operational log. Counts the last 24h. A
    /// disconnect is also logged when the PC sleeps or the user switches networks, so a count of
    /// 1-3 is only a Warn and the wording says so rather than claiming the Wi-Fi is faulty.
    /// </summary>
    public static CheckResult CheckWifiDisconnects()
    {
        const string log = "Microsoft-Windows-WLAN-AutoConfig/Operational";
        const string techCommand = "Get-WinEvent -LogName 'Microsoft-Windows-WLAN-AutoConfig/Operational' | Where-Object Id -in 8002,8003";
        try
        {
            var disconnects = CountEvents(log, "*[System[(EventID=8003) and TimeCreated[timediff(@SystemTime) <= 86400000]]]");
            var failures = CountEvents(log, "*[System[(EventID=8002) and TimeCreated[timediff(@SystemTime) <= 86400000]]]");
            var techDetail = $"Last 24h: {disconnects} disconnect event(s) (ID 8003), {failures} failed connection attempt(s) (ID 8002).";

            if (disconnects + failures == 0)
                return new CheckResult(CheckStatus.Pass, "Windows hasn't logged any Wi-Fi disconnects in the last 24 hours.", techCommand, techDetail);
            if (disconnects + failures <= 3)
                return new CheckResult(CheckStatus.Warn, $"Windows logged {disconnects + failures} Wi-Fi disconnect or failed-connection event{(disconnects + failures == 1 ? "" : "s")} in the last 24 hours - sleeping or switching networks also does this, so it may be normal.", techCommand, techDetail);
            return new CheckResult(CheckStatus.Bad, $"Windows logged {disconnects + failures} Wi-Fi disconnects or failed connections in the last 24 hours - your Wi-Fi really is dropping.", techCommand, techDetail);
        }
        catch (EventLogNotFoundException)
        {
            return new CheckResult(CheckStatus.Pass, "No Wi-Fi connection history is recorded on this PC - nothing to check here.", techCommand, $"Log '{log}' not found.");
        }
        catch (EventLogException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read your Wi-Fi connection history.", techCommand, $"EventLogException: {exception.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read your Wi-Fi connection history (access denied).", techCommand, "UnauthorizedAccessException.");
        }
    }

    /// <summary>One entry read from an event log, newest first. <see cref="Fields"/> are the event's data values in order.</summary>
    internal sealed record LoggedEvent(int Id, string Provider, DateTime Time, IReadOnlyList<string> Fields)
    {
        /// <summary>The event's data values by name, filled only when asked for (see <see cref="ReadEvents"/>).</summary>
        public IReadOnlyDictionary<string, string> NamedData { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Reads up to <paramref name="maxEvents"/> matching events, newest first. Throws the same exceptions as <see cref="EventLogReader"/>.
    /// Pass <paramref name="includeNamedData"/> to read values by name, which is safer than by position but slower.
    /// </summary>
    internal static IReadOnlyList<LoggedEvent> ReadEvents(string logName, string xpathQuery, int maxEvents = 500, bool includeNamedData = false)
    {
        var query = new EventLogQuery(logName, PathType.LogName, xpathQuery) { ReverseDirection = true };
        using var reader = new EventLogReader(query);

        var events = new List<LoggedEvent>();
        EventRecord? record;
        while (events.Count < maxEvents && (record = reader.ReadEvent()) is not null)
        {
            using (record)
            {
                var fields = record.Properties.Select(property => property.Value?.ToString() ?? string.Empty).ToList();
                events.Add(new LoggedEvent(record.Id, record.ProviderName ?? string.Empty, record.TimeCreated ?? DateTime.MinValue, fields)
                {
                    NamedData = includeNamedData ? ReadNamedData(record) : new Dictionary<string, string>(),
                });
            }
        }
        return events;
    }

    private static Dictionary<string, string> ReadNamedData(EventRecord record)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var document = System.Xml.Linq.XDocument.Parse(record.ToXml());
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Data"))
        {
            var name = element.Attribute("Name")?.Value;
            if (name is not null) data[name] = element.Value;
        }
        return data;
    }

    /// <summary>Names the program that crashed or froze most often in the last week, so "a program keeps crashing" gets an answer.</summary>
    public static CheckResult CheckRecentCrashes()
    {
        const string techCommand = "Get-WinEvent -LogName Application | Where-Object { $_.Id -in 1000,1002 } | Group-Object { $_.Properties[0].Value } | Sort-Object Count -Descending";
        const string applicationErrorId = "EventID=1000";
        const string applicationHangId = "EventID=1002";
        var query = $"*[System[Provider[@Name='Application Error' or @Name='Application Hang'] and ({applicationErrorId} or {applicationHangId}) and TimeCreated[timediff(@SystemTime) <= {CrashRules.WindowDays * MillisecondsPerDay}]]]";

        try
        {
            var events = ReadEvents("Application", query)
                .Select(logged => new CrashEvent(
                    Program: logged.Fields.Count > 0 ? logged.Fields[0] : "unknown program",
                    FaultingModule: logged.Id == 1000 && logged.Fields.Count > 3 ? logged.Fields[3] : "unknown",
                    WasFreeze: logged.Id == 1002))
                .ToList();

            return CrashRules.Summarize(events) with { TechCommand = techCommand, TechDetail = CrashRules.DescribeEvents(events) };
        }
        catch (EventLogException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Application event log.", techCommand, CheckText.DescribeException(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Application event log (access denied).", techCommand, CheckText.DescribeException(exception));
        }
    }

    private const int MillisecondsPerDay = 86_400_000;

    /// <summary>Errors from the last 24 hours, with Microsoft's documented harmless DistributedCOM noise set aside and the biggest real source named.</summary>
    public static CheckResult CheckRecentSystemErrors()
    {
        const string techCommand = "Get-WinEvent -FilterHashtable @{LogName='System';Level=2;StartTime=(Get-Date).AddDays(-1)} | Group-Object ProviderName,Id | Sort-Object Count -Descending";
        try
        {
            var errors = ReadEvents("System", $"*[System[(Level=2) and TimeCreated[timediff(@SystemTime) <= {MillisecondsPerDay}]]]", maxEvents: 2000)
                .Select(logged => new LoggedError(logged.Provider, logged.Id))
                .ToList();

            return ErrorRules.SummarizeSystemErrors(errors) with { TechCommand = techCommand, TechDetail = ErrorRules.DescribeSources(errors) };
        }
        catch (EventLogException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the System event log.", techCommand, CheckText.DescribeException(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the System event log (access denied).", techCommand, CheckText.DescribeException(exception));
        }
    }

    public static CheckResult CheckDiskErrors() => SafeCount(
        "System",
        "*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs']) and (Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]",
        count => count switch
        {
            0 => new CheckResult(CheckStatus.Pass, "No disk read/write errors in the last week."),
            <= 3 => new CheckResult(CheckStatus.Warn, $"{count} recoverable disk error{(count == 1 ? "" : "s")} found in the last week."),
            _ => new CheckResult(CheckStatus.Bad, $"{count} disk errors found in the last week - your drive may be failing."),
        },
        "Couldn't read the System event log for disk errors.",
        "Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='disk','Ntfs';Level=1,2;StartTime=(Get-Date).AddDays(-7)}");
}
