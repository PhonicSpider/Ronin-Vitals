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
            .Select(i => new CheckDefinition
            {
                Label = $"Check {i}",
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
        var vm = new SymptomCardViewModel(symptom, _ => { }, () => { });

        Assert.Equal("test-symptom", vm.Id);
        Assert.Equal("Test symptom", vm.Title);
        Assert.Equal("Test subtitle", vm.Subtitle);
        Assert.True(vm.IsCatchAll);
        Assert.Equal(3, vm.CheckCount);
        Assert.Equal(3, vm.Checks.Count);
    }

    // ── Expand / Collapse ────────────────────────────────────────────────────

    [Fact]
    public void ExpandCommand_InvokesRequestExpandWhenNotAlreadyExpanded()
    {
        SymptomCardViewModel? requested = null;
        var vm = new SymptomCardViewModel(MakeSymptom(), target => requested = target, () => { });

        vm.ExpandCommand.Execute(null);

        Assert.Same(vm, requested);
    }

    [Fact]
    public void ExpandCommand_DoesNothingWhenAlreadyExpanded()
    {
        var callCount = 0;
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => callCount++, () => { });
        vm.IsExpanded = true;

        vm.ExpandCommand.Execute(null);

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void CollapseCommand_InvokesRequestCollapse()
    {
        var collapsed = false;
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => collapsed = true);

        vm.CollapseCommand.Execute(null);

        Assert.True(collapsed);
    }

    // ── RunButtonLabel ───────────────────────────────────────────────────────

    [Fact]
    public void RunButtonLabel_StartsAsRunDiagnostic()
    {
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        Assert.Equal("Run diagnostic", vm.RunButtonLabel);
    }

    [Fact]
    public void RunButtonLabel_ShowsCheckingWhileRunning()
    {
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        vm.IsRunning = true;
        Assert.Equal("Checking…", vm.RunButtonLabel);
    }

    [Fact]
    public void RunButtonLabel_ShowsRunAgainAfterFirstRun()
    {
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        vm.HasRunOnce = true;
        Assert.Equal("Run again", vm.RunButtonLabel);
    }

    // ── RunDiagnosticAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RunDiagnosticAsync_AllPassingChecksProduceNoIssuesSummary()
    {
        var vm = new SymptomCardViewModel(MakeSymptom(checkCount: 1), _ => { }, () => { });

        await vm.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.True(vm.HasRunOnce);
        Assert.False(vm.IsRunning);
        Assert.True(vm.ShowSummary);
        Assert.False(vm.SummaryHasIssues);
        Assert.Contains("no issues found", vm.SummaryText);
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
        var vm = new SymptomCardViewModel(symptom, _ => { }, () => { });

        await vm.RunDiagnosticCommand.ExecuteAsync(null);

        Assert.True(vm.SummaryHasIssues);
        Assert.Contains("1 thing", vm.SummaryText);
        Assert.Equal(CheckStatus.Bad, vm.Checks[0].Status);
    }

    [Fact]
    public void ResetRun_ClearsRunStateAndEachCheck()
    {
        var vm = new SymptomCardViewModel(MakeSymptom(), _ => { }, () => { });
        vm.IsRunning = true;
        vm.HasRunOnce = true;
        vm.ShowSummary = true;

        vm.ResetRun();

        Assert.False(vm.IsRunning);
        Assert.False(vm.HasRunOnce);
        Assert.False(vm.ShowSummary);
        Assert.Equal(CheckStatus.Pending, vm.Checks[0].Status);
    }
}
