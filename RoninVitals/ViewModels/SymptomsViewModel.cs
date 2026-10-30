using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class SymptomsViewModel : ObservableObject
{
    private readonly Action _navigateHome;

    public SymptomsViewModel(SectionKey sectionKey, Action navigateHome)
    {
        _navigateHome = navigateHome;
        var section = VitalsCatalog.GetSection(sectionKey);
        SectionLabel = section.Label.ToUpperInvariant();
        Headline = section.Headline;

        Cards = new ObservableCollection<SymptomCardViewModel>(
            section.Symptoms.Select(symptom => new SymptomCardViewModel(symptom, ExpandCard, CollapseExpanded)));
    }

    public string SectionLabel { get; }
    public string Headline { get; }
    public ObservableCollection<SymptomCardViewModel> Cards { get; }

    [ObservableProperty] private SymptomCardViewModel? _expandedCard;

    public string BackLabel => ExpandedCard is null ? "All areas" : "All symptoms";

    /// <summary>Only meaningful on the symptom-picker view - hidden once a card is expanded.</summary>
    public bool ShowTechToggle => ExpandedCard is null;

    /// <summary>Passthrough to the app-wide TechModeService singleton (see its doc comment for
    /// why this isn't just a local property) - a plain get/set is enough since this toggle is
    /// the only thing that ever sets it, so there's no external change to reflect back.</summary>
    public bool IsTechMode
    {
        get => TechModeService.Instance.IsEnabled;
        set => TechModeService.Instance.IsEnabled = value;
    }

    private async void ExpandCard(SymptomCardViewModel target)
    {
        // Currently unreachable via the UI (siblings are hidden by IsSiblingCollapsed while one
        // card is expanded, so you can't click a different header without going through Back
        // first) - but reset defensively anyway so ExpandedCard never silently loses track of a
        // still-"expanded" card if this is ever reached some other way (keyboard nav, etc.).
        if (ExpandedCard is not null) ExpandedCard.IsExpanded = false;

        foreach (var card in Cards)
        {
            if (card == target) continue;
            card.IsSiblingCollapsed = true;
        }

        target.ResetRun();
        // Set before IsExpanded so the initial expand animation measures the small "gathering
        // information" view rather than the full checklist - see SymptomCardViewModel.
        target.IsGatheringInfo = true;
        target.IsExpanded = true;
        ExpandedCard = target;
        OnPropertyChanged(nameof(BackLabel));
        OnPropertyChanged(nameof(ShowTechToggle));

        await target.GatherInformationAsync();
    }

    private void CollapseExpanded()
    {
        if (ExpandedCard is null) return;
        ExpandedCard.IsExpanded = false;
        ExpandedCard.DisposeConsole();
        ExpandedCard = null;
        foreach (var card in Cards) card.IsSiblingCollapsed = false;
        OnPropertyChanged(nameof(BackLabel));
        OnPropertyChanged(nameof(ShowTechToggle));
    }

    [RelayCommand]
    private void Back()
    {
        if (ExpandedCard is not null)
        {
            CollapseExpanded();
        }
        else
        {
            _navigateHome();
        }
    }
}
