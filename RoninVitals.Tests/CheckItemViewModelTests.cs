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
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Warn));

        await vm.CompleteAsync();

        Assert.Equal(CheckStatus.Warn, vm.Status);
        Assert.Equal("Scripted result", vm.ResultText);
        Assert.True(vm.ShowResult);
    }

    [Fact]
    public async Task CompleteAsync_PrefersRealCheckOverScripted()
    {
        var vm = new CheckItemViewModel(MakeDefinition(
            scriptedStatus: CheckStatus.Pass,
            realCheck: () => new CheckResult(CheckStatus.Bad, "Real result")));

        await vm.CompleteAsync();

        Assert.Equal(CheckStatus.Bad, vm.Status);
        Assert.Equal("Real result", vm.ResultText);
    }

    // ── ShowFixArea ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowFixArea_FalseBeforeResultShown()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        Assert.False(vm.ShowFixArea);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ShowFixArea_FalseWhenStatusIsPass()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Pass, fixLabel: "Fix it"));
        await vm.CompleteAsync();
        Assert.False(vm.ShowFixArea);
    }

    [Fact]
    public async Task ShowFixArea_FalseWhenNoFixLabel()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: null));
        await vm.CompleteAsync();
        Assert.False(vm.ShowFixArea);
    }

    [Fact]
    public async Task ShowFixArea_TrueWhenNonPassResultShownAndFixLabelSet()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        await vm.CompleteAsync();
        Assert.True(vm.ShowFixArea);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_ClearsStatusResultAndFixState()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it"));
        await vm.CompleteAsync();

        vm.Reset();

        Assert.Equal(CheckStatus.Pending, vm.Status);
        Assert.Equal(string.Empty, vm.ResultText);
        Assert.False(vm.ShowResult);
        Assert.False(vm.FixApplied);
    }

    [Fact]
    public void Reset_ClearsGuidanceTextOpenedOnAPreviousRun()
    {
        // Regression: tips opened via an informational fix button stayed on screen after "Run again".
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Warn, fixLabel: "Show tips"));
        vm.FixMessage = "Some advice shown after clicking the fix button.";

        vm.Reset();

        Assert.Equal(string.Empty, vm.FixMessage);
        Assert.False(vm.HasFixMessage);
    }

    // ── ApplyFix (cosmetic path only - the real-fix path routes through
    // SafeFixService -> ActionLogService, which needs a live WPF Application
    // (Application.Current.Dispatcher) that a headless test host doesn't have.
    // Deliberately not covered here; see CHECKLIST.md. ──

    [Fact]
    public async Task ApplyFix_CosmeticFlipWhenNoApplyFixDelegate()
    {
        var vm = new CheckItemViewModel(MakeDefinition(scriptedStatus: CheckStatus.Bad, fixLabel: "Fix it", applyFix: null));

        await vm.ApplyFixCommand.ExecuteAsync(null);

        Assert.True(vm.FixApplied);
    }
}
