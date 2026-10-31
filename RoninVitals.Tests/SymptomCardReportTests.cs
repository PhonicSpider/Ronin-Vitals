using System.IO;
using RoninVitals.Models;
using RoninVitals.ViewModels;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>The "View report" button on a card. Shares ReportService's settable folder, so it runs one at a time.</summary>
[Collection(ReportServiceCollection.Name)]
public sealed class SymptomCardReportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RoninVitalsCardReportTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _originalFolder = ReportService.ReportsFolder;

    public SymptomCardReportTests() => ReportService.ReportsFolder = _folder;

    public void Dispose()
    {
        ReportService.ReportsFolder = _originalFolder;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static readonly ReportTable PortTable = new("Ports", ["Program", "Ports"], [new ReportRow(["steam.exe", "TCP 27036"])]);

    private static Symptom MakeSymptom(string? reportTitle) => new()
    {
        Id = "ports",
        Title = "Check open ports",
        Subtitle = "subtitle",
        ReportTitle = reportTitle,
        Checks =
        [
            new CheckDefinition
            {
                Label = "Looking at ports",
                ScriptedStatus = CheckStatus.Pass,
                ResultText = "scripted",
                FixInfoText = "Close the program you don't know.",
                RealCheck = () => new CheckResult(CheckStatus.Warn, "One program is open.", "Get-NetTCPConnection", "raw rows", [PortTable]),
            },
            new CheckDefinition
            {
                Label = "Looking at tasks",
                ScriptedStatus = CheckStatus.Pass,
                ResultText = "scripted",
                FixInfoText = "Not shown when passing.",
                RealCheck = () => new CheckResult(CheckStatus.Pass, "All routine."),
            },
        ],
    };

    private static SymptomCardViewModel MakeCard(string? reportTitle, Action<ReportPreviewViewModel>? showReport = null) =>
        new(MakeSymptom(reportTitle), _ => { }, () => { }, showReport);

    // ── When the button shows ───────────────────────────────────────────────

    [Fact]
    public void ShowReportButton_IsHiddenBeforeARunHasFinished() => Assert.False(MakeCard("Security report").ShowReportButton);

    [Fact]
    public async Task ShowReportButton_AppearsOnceARunFinishesOnACardThatOffersAReport()
    {
        var card = MakeCard("Security report");

        await card.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.True(card.ShowReportButton);
    }

    [Fact]
    public async Task ShowReportButton_NeverAppearsOnACardWithoutAReportTitle()
    {
        var card = MakeCard(null);

        await card.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.False(card.ShowReportButton);
    }

    [Fact]
    public async Task ShowReportButton_GoesAwayWhenTheCardIsResetForANewRun()
    {
        var card = MakeCard("Security report");
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        card.ResetRun();

        Assert.False(card.ShowReportButton);
    }

    [Fact]
    public async Task ShowReportButton_RaisesAChangeNotificationSoTheUiUpdates()
    {
        var card = MakeCard("Security report");
        var changed = new List<string?>();
        card.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await card.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.Contains(nameof(SymptomCardViewModel.ShowReportButton), changed);
    }

    // ── Viewing the report ──────────────────────────────────────────────────

    [Fact]
    public async Task ViewReport_ShowsWhatTheChecksFoundWithoutSavingAnything()
    {
        ReportPreviewViewModel? shown = null;
        var card = MakeCard("Security report", preview => shown = preview);
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        await card.ViewReportCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.False(Directory.Exists(_folder));
        Assert.Equal("Security report", shown.Title);
        Assert.Contains("One program is open.", shown.Html);
        Assert.Contains("steam.exe", shown.Html);
        Assert.Contains("TCP 27036", shown.Html);
        Assert.Contains("Close the program you don&#39;t know.", shown.Html);   // guidance for the check that needs attention
        Assert.DoesNotContain("Not shown when passing.", shown.Html);            // none for the check that passed
        Assert.Contains("Get-NetTCPConnection", shown.Html);
        Assert.Contains("Check open ports", shown.Html);                         // the card's title is the report's subtitle
    }

    [Fact]
    public async Task ViewReport_ShownReportCanBeSavedLater()
    {
        ReportPreviewViewModel? shown = null;
        var card = MakeCard("Security report", preview => shown = preview);
        await card.RunDiagnosticCommand.ExecuteAsync(null);
        await card.ViewReportCommand.ExecuteAsync(null);

        await shown!.SaveCommand.ExecuteAsync(null);

        var file = Assert.Single(Directory.GetFiles(_folder, "*.html"));
        Assert.Equal(shown.Html, File.ReadAllText(file));
    }

    [Fact]
    public async Task ViewReport_ButtonIsBusyOnlyWhilePreparingTheReport()
    {
        var card = MakeCard("Security report", _ => { });
        await card.RunDiagnosticCommand.ExecuteAsync(null);
        Assert.Equal("View report", card.ReportButtonLabel);

        await card.ViewReportCommand.ExecuteAsync(null);

        Assert.False(card.IsCreatingReport);
        Assert.Equal("View report", card.ReportButtonLabel);
    }

    [Fact]
    public async Task ViewReport_DoesNothingOnACardWithoutAReportTitle()
    {
        var shownCount = 0;
        var card = MakeCard(null, _ => shownCount++);
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        await card.ViewReportCommand.ExecuteAsync(null);

        Assert.Equal(0, shownCount);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task ViewReport_IsHarmlessWhenNothingIsListeningForTheReport()
    {
        var card = MakeCard("Security report");
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        await card.ViewReportCommand.ExecuteAsync(null);

        Assert.False(Directory.Exists(_folder));
    }

    // ── What a check hands to the report ────────────────────────────────────

    [Fact]
    public async Task ToReportEntry_CarriesTheTablesAndOnlyOffersGuidanceWhenNeeded()
    {
        var card = MakeCard("Security report");
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        var warning = card.Checks[0].ToReportEntry();
        var passing = card.Checks[1].ToReportEntry();

        Assert.Equal(CheckStatus.Warn, warning.Status);
        Assert.Equal([PortTable], warning.Tables);
        Assert.Equal("Close the program you don't know.", warning.Guidance);
        Assert.Equal("raw rows", warning.TechDetail);
        Assert.Null(passing.Guidance);
        Assert.Empty(passing.Tables);
    }

    [Fact]
    public async Task ResettingACheckForgetsItsTables()
    {
        var card = MakeCard("Security report");
        await card.RunDiagnosticCommand.ExecuteAsync(null);

        card.ResetRun();

        Assert.Empty(card.Checks[0].Tables);
    }
}
