using RoninVitals.Models;
using RoninVitals.ViewModels;

namespace RoninVitals.Tests;

public class SymptomCardViewModelTests
{
    private static Symptom MakeSymptom(int checkCount = 1, bool isCatchAll = false) => new()
    {
        Id = "test-symptom",
        Title = "Test symptom",
        Subtitle = "Test subtitle",
        IsCatchAll = isCatchAll,
        Checks = Enumerable.Range(0, checkCount)
            .Select(number => new CheckDefinition
            {
                Label = $"Check {number}",
                ScriptedStatus = CheckStatus.Pass,
                ResultText = "ok",
                RealCheck = () => new CheckResult(CheckStatus.Pass, "ok"),
            })
            .ToList(),
    };

    // ── Basic projection from the Symptom model ─────────────────────────────

    [Fact]
    public void Properties_ProjectFromSymptom()
    {
        var symptom = MakeSymptom(checkCount: 3, isCatchAll: true);
        var viewModel = new SymptomCardViewModel(symptom, _ => { }, () => { });

        Assert.Equal("test-symptom", viewModel.Id);
        Assert.Equal("Test symptom", viewModel.Title);
        Assert.Equal("Test subtitle", viewModel.Subtitle);
        Assert.True(viewModel.IsCatchAll);
        Assert.Equal(3, viewModel.CheckCount);
        Assert.Equal(3, viewModel.Checks.Count);
    }

    // ── Expand / Collapse ────────────────────────────────────────────────────

    [Fact]
    public void ExpandCommand_InvokesRequestExpandWhenNotAlreadyExpanded()
    {
        SymptomCardViewModel? requested = null;
        var viewModel = new SymptomCardViewModel(MakeSymptom(), target => requested = target, () => { });

        viewModel.ExpandCommand.Execute(null);

        Assert.Same(viewModel, requested);
    }

    [Fact]
    public void ExpandCommand_DoesNothingWhenAlreadyExpanded()
    {
        var callCount = 0;
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => callCount++, () => { });
        viewModel.IsExpanded = true;

        viewModel.ExpandCommand.Execute(null);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void CollapseCommand_InvokesRequestCollapse()
    {
        var collapsed = false;
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => collapsed = true);

        viewModel.CollapseCommand.Execute(null);

        Assert.True(collapsed);
    }

    // ── RunButtonLabel ───────────────────────────────────────────────────────

    [Fact]
    public void RunButtonLabel_StartsAsRunDiagnostic()
    {
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        Assert.Equal("Run diagnostic", viewModel.RunButtonLabel);
    }

    [Fact]
    public void RunButtonLabel_ShowsCheckingWhileRunning()
    {
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        viewModel.IsRunning = true;
        Assert.Equal("Checking…", viewModel.RunButtonLabel);
    }

    [Fact]
    public void RunButtonLabel_ShowsRunAgainAfterFirstRun()
    {
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        viewModel.HasRunOnce = true;
        Assert.Equal("Run again", viewModel.RunButtonLabel);
    }

    // ── RunDiagnosticAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RunDiagnosticAsync_AllPassingChecksProduceNoIssuesSummary()
    {
        var viewModel = new SymptomCardViewModel(MakeSymptom(checkCount: 1), _ => { }, () => { });

        await viewModel.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRunOnce);
        Assert.False(viewModel.IsRunning);
        Assert.True(viewModel.ShowSummary);
        Assert.False(viewModel.SummaryHasIssues);
        Assert.Contains("no issues found", viewModel.SummaryText);
    }

    [Fact]
    public async Task RunDiagnosticAsync_FailingCheckProducesIssuesSummary()
    {
        var symptom = new Symptom
        {
            Id = "bad-symptom",
            Title = "Bad symptom",
            Subtitle = "Subtitle",
            Checks = new List<CheckDefinition>
            {
                new()
                {
                    Label = "Failing check",
                    ScriptedStatus = CheckStatus.Bad,
                    ResultText = "scripted",
                    RealCheck = () => new CheckResult(CheckStatus.Bad, "it's broken"),
                },
            },
        };
        var viewModel = new SymptomCardViewModel(symptom, _ => { }, () => { });

        await viewModel.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.True(viewModel.SummaryHasIssues);
        Assert.Contains("1 thing", viewModel.SummaryText);
        Assert.Equal(CheckStatus.Bad, viewModel.Checks[0].Status);
    }

    [Fact]
    public void ResetRun_ClearsRunStateAndEachCheck()
    {
        var viewModel = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        viewModel.IsRunning = true;
        viewModel.HasRunOnce = true;
        viewModel.ShowSummary = true;

        viewModel.ResetRun();

        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.HasRunOnce);
        Assert.False(viewModel.ShowSummary);
        Assert.Equal(CheckStatus.Pending, viewModel.Checks[0].Status);
    }
}
