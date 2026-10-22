using RoninVitals.Models;
using RoninVitals.ViewModels;

namespace RoninVitals.Tests;

/// <summary>
/// Covers the in-place expansion/collapse coordination (checklist item: "Symptoms screen -
/// in-place card expansion, siblings collapse"). Uses the real VitalsCatalog data since that's
/// pure static content with no OS calls - only RunDiagnosticCommand would touch real checks,
/// and these tests never run diagnostics, just navigate expand/collapse state.
/// </summary>
public class SymptomsViewModelTests
{
    static SymptomsViewModelTests()
    {
        // ExpandCommand now kicks off SymptomCardViewModel.GatherInformationAsync for real (it's
        // production code, not a mock) - which pays a real ~10s pacing floor. These tests care
        // about expand/collapse state transitions, not that delay, so zero it out for the suite.
        SymptomCardViewModel.GatheringMinimumDuration = TimeSpan.Zero;
    }

    private static SymptomsViewModel MakeViewModel() => new(SectionKey.Network, () => { });

    [Fact]
    public void Constructor_LoadsCardsFromTheRealCatalogSection()
    {
        var vm = MakeViewModel();
        Assert.Equal("NETWORK", vm.SectionLabel);
        Assert.NotEmpty(vm.Cards);
    }

    [Fact]
    public void BackLabel_IsAllAreasWhenNothingExpanded()
    {
        var vm = MakeViewModel();
        Assert.Equal("All areas", vm.BackLabel);
    }

    [Fact]
    public void ExpandingACard_CollapsesAllSiblingsAndUpdatesBackLabel()
    {
        var vm = MakeViewModel();
        var target = vm.Cards[0];

        target.ExpandCommand.Execute(null);

        Assert.True(target.IsExpanded);
        Assert.False(target.IsSiblingCollapsed);
        Assert.Same(target, vm.ExpandedCard);
        Assert.Equal("All symptoms", vm.BackLabel);

        foreach (var sibling in vm.Cards.Where(c => c != target))
            Assert.True(sibling.IsSiblingCollapsed);
    }

    [Fact]
    public void CollapsingTheExpandedCard_RestoresAllSiblingsAndBackLabel()
    {
        var vm = MakeViewModel();
        var target = vm.Cards[0];
        target.ExpandCommand.Execute(null);

        target.CollapseCommand.Execute(null);

        Assert.False(target.IsExpanded);
        Assert.Null(vm.ExpandedCard);
        Assert.Equal("All areas", vm.BackLabel);

        foreach (var card in vm.Cards)
            Assert.False(card.IsSiblingCollapsed);
    }

    [Fact]
    public void BackCommand_CollapsesExpandedCardInsteadOfNavigatingHome()
    {
        var navigatedHome = false;
        var vm = new SymptomsViewModel(SectionKey.Network, () => navigatedHome = true);
        vm.Cards[0].ExpandCommand.Execute(null);

        vm.BackCommand.Execute(null);

        Assert.False(navigatedHome);
        Assert.Null(vm.ExpandedCard);
    }

    [Fact]
    public void BackCommand_NavigatesHomeWhenNothingExpanded()
    {
        var navigatedHome = false;
        var vm = new SymptomsViewModel(SectionKey.Network, () => navigatedHome = true);

        vm.BackCommand.Execute(null);

        Assert.True(navigatedHome);
    }

    [Fact]
    public void ExpandingASecondCard_MovesExpansionRatherThanStacking()
    {
        var vm = MakeViewModel();
        if (vm.Cards.Count < 2) return; // Network section always has >1 symptom, but guard anyway.

        vm.Cards[0].ExpandCommand.Execute(null);
        vm.Cards[1].ExpandCommand.Execute(null);

        Assert.False(vm.Cards[0].IsExpanded);
        Assert.True(vm.Cards[1].IsExpanded);
        Assert.Same(vm.Cards[1], vm.ExpandedCard);
    }
}
