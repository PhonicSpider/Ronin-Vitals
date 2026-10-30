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
        var viewModel = MakeViewModel();
        Assert.Equal("NETWORK", viewModel.SectionLabel);
        Assert.NotEmpty(viewModel.Cards);
    }

    [Fact]
    public void BackLabel_IsAllAreasWhenNothingExpanded()
    {
        var viewModel = MakeViewModel();
        Assert.Equal("All areas", viewModel.BackLabel);
    }

    [Fact]
    public void ExpandingACard_CollapsesAllSiblingsAndUpdatesBackLabel()
    {
        var viewModel = MakeViewModel();
        var target = viewModel.Cards[0];

        target.ExpandCommand.Execute(null);

        Assert.True(target.IsExpanded);
        Assert.False(target.IsSiblingCollapsed);
        Assert.Same(target, viewModel.ExpandedCard);
        Assert.Equal("All symptoms", viewModel.BackLabel);

        foreach (var sibling in viewModel.Cards.Where(card => card != target))
            Assert.True(sibling.IsSiblingCollapsed);
    }

    [Fact]
    public void CollapsingTheExpandedCard_RestoresAllSiblingsAndBackLabel()
    {
        var viewModel = MakeViewModel();
        var target = viewModel.Cards[0];
        target.ExpandCommand.Execute(null);

        target.CollapseCommand.Execute(null);

        Assert.False(target.IsExpanded);
        Assert.Null(viewModel.ExpandedCard);
        Assert.Equal("All areas", viewModel.BackLabel);

        foreach (var card in viewModel.Cards)
            Assert.False(card.IsSiblingCollapsed);
    }

    [Fact]
    public void BackCommand_CollapsesExpandedCardInsteadOfNavigatingHome()
    {
        var navigatedHome = false;
        var viewModel = new SymptomsViewModel(SectionKey.Network, () => navigatedHome = true);
        viewModel.Cards[0].ExpandCommand.Execute(null);

        viewModel.BackCommand.Execute(null);

        Assert.False(navigatedHome);
        Assert.Null(viewModel.ExpandedCard);
    }

    [Fact]
    public void BackCommand_NavigatesHomeWhenNothingExpanded()
    {
        var navigatedHome = false;
        var viewModel = new SymptomsViewModel(SectionKey.Network, () => navigatedHome = true);

        viewModel.BackCommand.Execute(null);

        Assert.True(navigatedHome);
    }

    [Fact]
    public void ExpandingASecondCard_MovesExpansionRatherThanStacking()
    {
        var viewModel = MakeViewModel();
        if (viewModel.Cards.Count < 2) return; // Network section always has >1 symptom, but guard anyway.

        viewModel.Cards[0].ExpandCommand.Execute(null);
        viewModel.Cards[1].ExpandCommand.Execute(null);

        Assert.False(viewModel.Cards[0].IsExpanded);
        Assert.True(viewModel.Cards[1].IsExpanded);
        Assert.Same(viewModel.Cards[1], viewModel.ExpandedCard);
    }
}
