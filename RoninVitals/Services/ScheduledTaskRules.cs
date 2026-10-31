using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>One way a task starts. <paramref name="Starts"/> and <paramref name="Ends"/> are the task's own activation and expiry times, when it sets them.</summary>
public sealed record TaskTrigger(string Description, DateTime? Starts, DateTime? Ends);

public sealed record TaskAction(string Command, string Arguments);

/// <summary>A task from the Windows Task Scheduler library, read from its XML definition.</summary>
public sealed record ScheduledTask(
    string Path,
    string Name,
    bool IsEnabled,
    string? Author,
    string? Description,
    string RunsAs,
    IReadOnlyList<TaskTrigger> Triggers,
    IReadOnlyList<TaskAction> Actions,
    TimeSpan? StopsAfter,
    DateTime? LastRun,
    DateTime? NextRun,
    int LastResult)
{
    /// <summary>Turned on and set to start by itself at least one way. Tasks that only run when something else calls them are not "scheduled".</summary>
    public bool IsScheduled => IsEnabled && Triggers.Count > 0;

    public bool IsFromWindows => Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads Task Scheduler definitions and judges them, kept apart from the code that talks to the Task Scheduler so it can be
/// tested with sample XML. A scheduled task is how most software (and a good deal of malware) keeps itself running, so the
/// check lists them all and only raises a concern for the patterns that rarely appear in honest software.
/// </summary>
public static partial class ScheduledTaskRules
{
    private const int OleZeroYear = 1900; // Task Scheduler reports "never" as 30 Dec 1899
    private const int NeverRunResult = 0x41303;
    private const int RunningResult = 0x41301;
    private static readonly TimeSpan DefaultStopAfter = TimeSpan.FromHours(72);

    private static readonly Dictionary<string, string> KnownAccounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-5-4"] = "Interactive users",
        ["S-1-5-18"] = "SYSTEM",
        ["S-1-5-19"] = "Local Service",
        ["S-1-5-20"] = "Network Service",
        ["S-1-5-32-544"] = "Administrators",
        ["S-1-5-32-545"] = "Users",
        ["S-1-1-0"] = "Everyone",
    };

    [GeneratedRegex(@"(powershell|pwsh)(\.exe)?\b.*\s-(e|ec|enc|encodedcommand)\s+[A-Za-z0-9+/=]{20,}", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedPowerShell();

    [GeneratedRegex(@"-w(indowstyle)?\s+hidden", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenWindow();

    [GeneratedRegex(@"downloadstring|downloadfile|invoke-webrequest|invoke-expression|\biex\b|bitsadmin|certutil.*-urlcache", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadsOrRunsText();

    // ── Reading one task ────────────────────────────────────────────────────────────────────

    /// <param name="resolveText">Turns an indirect string such as <c>@%SystemRoot%\system32\x.dll,-100</c> into its text, or null when it can't.</param>
    /// <param name="resolveAccount">Turns a security ID such as <c>S-1-5-21-...</c> into an account name, or null when it can't.</param>
    public static ScheduledTask Parse(
        string path, string name, bool isRegisteredEnabled, DateTime? lastRun, DateTime? nextRun, int lastResult,
        string xml, Func<string, string?> resolveText, Func<string, string?> resolveAccount)
    {
        var task = LoadXml(xml);
        var registration = Child(task, "RegistrationInfo");
        var settings = Child(task, "Settings");

        var isEnabled = isRegisteredEnabled && !IsFalse(settings, "Enabled");
        var triggers = Child(task, "Triggers")?.Elements()
            .Where(trigger => !IsFalse(trigger, "Enabled"))
            .Select(trigger => ReadTrigger(trigger))
            .ToList() ?? [];
        var actions = Child(task, "Actions")?.Elements().Select(ReadAction).OfType<TaskAction>().ToList() ?? [];

        return new ScheduledTask(
            Path: path,
            Name: name,
            IsEnabled: isEnabled,
            Author: ResolveReference(TextOf(registration, "Author"), resolveText),
            Description: ResolveReference(TextOf(registration, "Description"), resolveText),
            RunsAs: ReadRunsAs(Child(Child(task, "Principals"), "Principal"), resolveAccount),
            Triggers: triggers,
            Actions: actions,
            StopsAfter: ReadStopAfter(settings),
            LastRun: ToRealTime(lastRun),
            NextRun: ToRealTime(nextRun),
            LastResult: lastResult);
    }

    private static XElement LoadXml(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader).Root ?? throw new XmlException("The task has no content.");
    }

    // XML namespaces differ between Task Scheduler versions, so elements are matched by local name.
    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);

    private static string? TextOf(XElement? parent, string localName)
    {
        var text = Child(parent, localName)?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool IsFalse(XElement? parent, string localName) =>
        string.Equals(TextOf(parent, localName), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Descriptions and authors are often references like <c>$(@%SystemRoot%\system32\x.dll,-100)</c> into a Windows resource file.</summary>
    public static string? ResolveReference(string? text, Func<string, string?> resolveText)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var reference = text.Trim();
        if (reference.StartsWith("$(", StringComparison.Ordinal) && reference.EndsWith(')')) reference = reference[2..^1];
        if (!reference.StartsWith('@')) return reference;

        var resolved = resolveText(reference);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved.Trim();
    }

    private static string ReadRunsAs(XElement? principal, Func<string, string?> resolveAccount)
    {
        var account = TextOf(principal, "UserId") ?? TextOf(principal, "GroupId");
        if (account is null) return "The user who created it";

        var friendly = KnownAccounts.TryGetValue(account, out var known) ? known
            : account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase) ? resolveAccount(account) ?? account
            : account;
        return string.Equals(TextOf(principal, "RunLevel"), "HighestAvailable", StringComparison.OrdinalIgnoreCase)
            ? $"{friendly} (highest privileges)"
            : friendly;
    }

    private static TimeSpan? ReadStopAfter(XElement? settings)
    {
        var text = TextOf(settings, "ExecutionTimeLimit");
        if (text is null) return DefaultStopAfter;

        var limit = ParseDuration(text);
        return limit is null || limit == TimeSpan.Zero ? null : limit; // PT0S means "never stop it"
    }

    private static TaskAction? ReadAction(XElement action)
    {
        switch (action.Name.LocalName)
        {
            case "Exec":
                var command = TextOf(action, "Command");
                return command is null ? null : new TaskAction(Environment.ExpandEnvironmentVariables(command), TextOf(action, "Arguments") ?? string.Empty);
            case "ComHandler":
                return new TaskAction($"COM handler {TextOf(action, "ClassId") ?? "(unknown)"}", TextOf(action, "Data") ?? string.Empty);
            default:
                return null;
        }
    }

    // ── Triggers ────────────────────────────────────────────────────────────────────────────

    private static TaskTrigger ReadTrigger(XElement trigger)
    {
        var starts = ParseBoundary(TextOf(trigger, "StartBoundary"));
        var ends = ParseBoundary(TextOf(trigger, "EndBoundary"));
        var timeOfDay = starts is { } start ? $" at {start:HH:mm}" : string.Empty;

        var description = trigger.Name.LocalName switch
        {
            "TimeTrigger" => $"Once{timeOfDay}",
            "CalendarTrigger" => DescribeCalendar(trigger) + timeOfDay,
            "BootTrigger" => "When Windows starts" + DescribeDelay(trigger),
            "LogonTrigger" => $"When {TextOf(trigger, "UserId") ?? "any user"} signs in" + DescribeDelay(trigger),
            "IdleTrigger" => "When the PC is idle",
            "EventTrigger" => "When a particular Windows event is logged",
            "RegistrationTrigger" => "When the task is created or changed",
            "SessionStateChangeTrigger" => "When a user session connects, disconnects, locks or unlocks",
            var other => other.EndsWith("Trigger", StringComparison.Ordinal) ? other[..^"Trigger".Length] : other,
        };

        return new TaskTrigger(description + DescribeRepetition(Child(trigger, "Repetition")), starts, ends);
    }

    private static string DescribeCalendar(XElement trigger)
    {
        if (Child(trigger, "ScheduleByDay") is { } daily)
        {
            var interval = ReadWholeNumber(daily, "DaysInterval", 1);
            return interval == 1 ? "Every day" : $"Every {interval} days";
        }

        if (Child(trigger, "ScheduleByWeek") is { } weekly)
        {
            var interval = ReadWholeNumber(weekly, "WeeksInterval", 1);
            var days = Child(weekly, "DaysOfWeek")?.Elements().Select(day => day.Name.LocalName[..3]).ToList() ?? [];
            var every = interval == 1 ? "Every week" : $"Every {interval} weeks";
            return days.Count == 0 ? every : $"{every} on {string.Join(", ", days)}";
        }

        if (Child(trigger, "ScheduleByMonth") is not null) return "Every month";
        if (Child(trigger, "ScheduleByMonthDayOfWeek") is not null) return "Every month, on set weekdays";
        return "On a calendar schedule";
    }

    private static int ReadWholeNumber(XElement parent, string localName, int fallback) =>
        int.TryParse(TextOf(parent, localName), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : fallback;

    private static string DescribeDelay(XElement trigger)
    {
        var delay = ParseDuration(TextOf(trigger, "Delay"));
        return delay is { } wait && wait > TimeSpan.Zero ? $", after {FormatDuration(wait)}" : string.Empty;
    }

    private static string DescribeRepetition(XElement? repetition)
    {
        var interval = ParseDuration(TextOf(repetition, "Interval"));
        if (interval is null || interval == TimeSpan.Zero) return string.Empty;

        var duration = ParseDuration(TextOf(repetition, "Duration"));
        var span = duration is { } length && length > TimeSpan.Zero ? $"for {FormatDuration(length)}" : "with no end";
        return $", repeating every {FormatDuration(interval.Value)} {span}";
    }

    // ── Dates and durations ─────────────────────────────────────────────────────────────────

    /// <summary>Task Scheduler writes boundaries as ISO 8601 text, with a UTC offset only when the task has one. Without one it means local time.</summary>
    public static DateTime? ParseBoundary(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var moment) ? moment.LocalDateTime : null;

    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return XmlConvert.ToTimeSpan(text.Trim()); }
        catch (Exception exception) when (exception is FormatException or OverflowException) { return null; }
    }

    public static string FormatDuration(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days} {Plural(span.Days, "day", "days")}");
        if (span.Hours > 0) parts.Add($"{span.Hours} {Plural(span.Hours, "hour", "hours")}");
        if (span.Minutes > 0) parts.Add($"{span.Minutes} {Plural(span.Minutes, "minute", "minutes")}");
        if (span.Seconds > 0 && parts.Count == 0) parts.Add($"{span.Seconds} {Plural(span.Seconds, "second", "seconds")}");
        return parts.Count == 0 ? "0 seconds" : string.Join(" ", parts);
    }

    private static DateTime? ToRealTime(DateTime? time) => time is { } value && value.Year >= OleZeroYear ? value : null;

    public static string FormatTime(DateTime time) => time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string DescribeLastResult(ScheduledTask task) => task.LastResult switch
    {
        0 => "succeeded",
        NeverRunResult => "has not run yet",
        RunningResult => "running now",
        var code => $"ended with error 0x{code:X}",
    };

    // ── Things worth a second look ──────────────────────────────────────────────────────────

    /// <summary>The few patterns that rarely appear in honest software. Anything else is only listed, never flagged.</summary>
    public static IReadOnlyList<string> FindConcerns(ScheduledTask task)
    {
        var concerns = new List<string>();
        foreach (var action in task.Actions)
        {
            var commandLine = $"{action.Command} {action.Arguments}";
            if (SuspiciousFileRules.IsRiskyLaunchLocation(SuspiciousFileRules.ExtractExecutablePath(action.Command)) && !concerns.Contains(StartsFromOddFolder))
                concerns.Add(StartsFromOddFolder);
            if (EncodedPowerShell().IsMatch(commandLine) && !concerns.Contains(HidesEncodedCommand))
                concerns.Add(HidesEncodedCommand);
            if (HiddenWindow().IsMatch(commandLine) && DownloadsOrRunsText().IsMatch(commandLine) && !concerns.Contains(HidesDownload))
                concerns.Add(HidesDownload);
        }
        return concerns;
    }

    private const string StartsFromOddFolder = "starts a program from a Temp, Downloads or other unusual folder";
    private const string HidesEncodedCommand = "runs an encoded PowerShell command, which hides what it does";
    private const string HidesDownload = "runs a hidden window that downloads or runs text";

    // ── Judging and tabulating ──────────────────────────────────────────────────────────────

    public static CheckResult Judge(IReadOnlyList<ScheduledTask> tasks, int unreadableCount)
    {
        if (tasks.Count == 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't read any scheduled tasks. Windows may have blocked the Task Scheduler, or Vitals needs administrator rights.");

        var scheduled = tasks.Where(task => task.IsScheduled).ToList();
        var fromWindows = scheduled.Count(task => task.IsFromWindows);
        var fromPrograms = scheduled.Where(task => !task.IsFromWindows).ToList();
        var unreadableNote = unreadableCount > 0 ? $" {unreadableCount} {Plural(unreadableCount, "task", "tasks")} couldn't be read." : string.Empty;

        var flagged = scheduled.Where(task => FindConcerns(task).Count > 0).ToList();
        if (flagged.Count > 0)
        {
            var reasons = flagged.SelectMany(FindConcerns).Distinct().ToList();
            return new CheckResult(CheckStatus.Warn,
                $"{flagged.Count} scheduled {Plural(flagged.Count, "task looks", "tasks look")} unusual (for example {Examples(flagged.Select(task => task.Name))}). " +
                $"Why: {string.Join("; ", reasons)}. That's rare in honest software. The report lists each one.{unreadableNote}");
        }

        var programExamples = fromPrograms.Count == 0 ? string.Empty : $", for example {Examples(fromPrograms.Select(task => task.Name))}";
        return new CheckResult(CheckStatus.Pass,
            $"{scheduled.Count} {Plural(scheduled.Count, "task is", "tasks are")} set to run by themselves: {fromWindows} from Windows and " +
            $"{fromPrograms.Count} added by other programs{programExamples}. None start programs from Temp or Downloads or hide encoded commands. " +
            $"The report lists them all with their times.{unreadableNote}");
    }

    private static readonly string[] ScheduleColumns =
        ["Task", "What it does", "Runs", "Starts when", "Next run", "Last run", "Runs as", "Stops if still running after"];

    public static IReadOnlyList<ReportTable> BuildTables(IReadOnlyList<ScheduledTask> tasks)
    {
        var scheduled = tasks.Where(task => task.IsScheduled).OrderBy(task => task.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var offCount = tasks.Count - scheduled.Count;
        var tables = new List<ReportTable>();

        var fromPrograms = scheduled.Where(task => !task.IsFromWindows).ToList();
        if (fromPrograms.Count > 0)
        {
            tables.Add(new ReportTable(
                Title: "Scheduled tasks added by programs other than Windows",
                Columns: ScheduleColumns,
                Rows: fromPrograms.Select(ToRow).ToList(),
                Note: "Updaters, backup tools, launchers and cleanup jobs are the usual owners. Rows marked for attention start a program from an odd folder or hide what they run. " +
                      "To switch one off, open Task Scheduler, right-click it and choose Disable (which you can undo) rather than deleting it."));
        }

        var fromWindows = scheduled.Where(task => task.IsFromWindows).ToList();
        if (fromWindows.Count > 0)
        {
            tables.Add(new ReportTable(
                Title: "Scheduled tasks that belong to Windows",
                Columns: ScheduleColumns,
                Rows: fromWindows.Select(ToRow).ToList(),
                Note: $"Windows' own maintenance, update and diagnostic jobs. {offCount} other {Plural(offCount, "task is", "tasks are")} turned off or only run when something else starts {Plural(offCount, "it", "them")}, so {Plural(offCount, "it isn't", "they aren't")} listed.",
                StartsCollapsed: true));
        }
        return tables;
    }

    private static ReportRow ToRow(ScheduledTask task)
    {
        var concerns = FindConcerns(task);
        var name = concerns.Count == 0 ? task.Path : $"{task.Path}\nNeeds attention: {concerns[0]}";
        var lastRun = task.LastRun is { } last ? $"{FormatTime(last)}\n{DescribeLastResult(task)}" : DescribeLastResult(task);

        return new ReportRow(
            [
                name,
                task.Description ?? "No description",
                task.Actions.Count == 0 ? "Nothing" : string.Join("\n", task.Actions.Select(DescribeAction)),
                string.Join("\n", task.Triggers.Select(DescribeTrigger)),
                task.NextRun is { } next ? FormatTime(next) : "Not planned",
                lastRun,
                task.RunsAs,
                task.StopsAfter is { } limit ? FormatDuration(limit) : "Never stopped automatically",
            ],
            concerns.Count > 0 ? ReportRowEmphasis.Notable : task.IsFromWindows ? ReportRowEmphasis.Muted : ReportRowEmphasis.Normal);
    }

    private static string DescribeAction(TaskAction action) =>
        string.IsNullOrWhiteSpace(action.Arguments) ? action.Command : $"{action.Command} {action.Arguments}";

    private static string DescribeTrigger(TaskTrigger trigger)
    {
        var lines = new List<string> { trigger.Description };
        if (trigger.Starts is { } starts) lines.Add($"Active from {FormatTime(starts)}");
        if (trigger.Ends is { } ends) lines.Add($"Stops {FormatTime(ends)}");
        return string.Join("\n", lines);
    }
}
