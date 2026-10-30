using RoninVitals.Models;
using RoninVitals.ViewModels;

namespace RoninVitals.Tests;

public class CheckItemViewModelTests
{
    private static CheckDefinition MakeDefinition(
        CheckStatus scriptedStatus = CheckStatus.Pass,
        string? fixLabel = null,
        Func<CheckResult>? realCheck = null,
        Func<Task<bool>>? applyFix = null) => new()
    {
        Label = "Test check",
        ScriptedStatus = scriptedStatus,
        ResultText = "Scripted result",
        FixLabel = fixLabel,
        RealCheck = realCheck,
        ApplyFix = applyFix,
    };

    // ── CompleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_UsesScriptedResultWhenNoRealCheck()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Warn));

        await viewModel.CompleteAsync();

        Assert.Equal(CheckStatus.Warn, viewModel.Status);
        Assert.Equal("Scripted result", viewModel.ResultText);
        Assert.True(viewModel.ShowResult);
    }

    [Fact]
    public async Task CompleteAsync_PrefersRealCheckOverScripted()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(
            scriptedStatus: CheckStatus.Pass,
            realCheck: () => new CheckResult(CheckStatus.Bad, "Real result")));

        await viewModel.CompleteAsync();

        Assert.Equal(CheckStatus.Bad, viewModel.Status);
        Assert.Equal("Real result", viewModel.ResultText);
    }

    // ── ShowFixArea ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowFixArea_FalseBeforeResultShown()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        Assert.False(viewModel.ShowFixArea);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ShowFixArea_FalseWhenStatusIsPass()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Pass, fixLabel: "Fix it"));
        await viewModel.CompleteAsync();
        Assert.False(viewModel.ShowFixArea);
    }

    [Fact]
    public async Task ShowFixArea_FalseWhenNoFixLabel()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: null));
        await viewModel.CompleteAsync();
        Assert.False(viewModel.ShowFixArea);
    }

    [Fact]
    public async Task ShowFixArea_TrueWhenNonPassResultShownAndFixLabelSet()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        await viewModel.CompleteAsync();
        Assert.True(viewModel.ShowFixArea);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_ClearsStatusResultAndFixState()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        await viewModel.CompleteAsync();

        viewModel.Reset();

        Assert.Equal(CheckStatus.Pending, viewModel.Status);
        Assert.Equal(string.Empty, viewModel.ResultText);
        Assert.False(viewModel.ShowResult);
        Assert.False(viewModel.FixApplied);
    }

    [Fact]
    public void Reset_ClearsGuidanceTextOpenedOnAPreviousRun()
    {
        // Regression: tips opened via an informational fix button stayed on screen after "Run again".
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Warn, fixLabel: "Show tips"));
        viewModel.FixMessage = "Some advice shown after clicking the fix button.";

        viewModel.Reset();

        Assert.Equal(string.Empty, viewModel.FixMessage);
        Assert.False(viewModel.HasFixMessage);
    }

    // ── ApplyFix (cosmetic path only - the real-fix path routes through
    // SafeFixService -> ActionLogService, which needs a live WPF Application
    // (Application.Current.Dispatcher) that a headless test host doesn't have.
    // Deliberately not covered here; see CHECKLIST.md. ──

    [Fact]
    public async Task ApplyFix_CosmeticFlipWhenNoApplyFixDelegate()
    {
        var viewModel = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it", applyFix: null));

        await viewModel.ApplyFixCommand.ExecuteAsync(null);

        Assert.True(viewModel.FixApplied);
    }
}
