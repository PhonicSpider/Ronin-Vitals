using CommunityToolkit.Mvvm.ComponentModel;

namespace RoninVitals.Services;

/// <summary>
/// Whether Tech Mode is on, app-wide. Lives outside any screen's view model (a singleton, like
/// ActionLogService) because SymptomsViewModel/SymptomCardViewModel are recreated fresh on every
/// navigation, but the toggle - set once on a section's symptom-picker screen - needs to still
/// be in effect when the user goes on to expand a card and run diagnostics.
/// </summary>
public sealed partial class TechModeService : ObservableObject
{
    public static TechModeService Instance { get; } = new();

    private TechModeService() { }

    [ObservableProperty] private bool _isEnabled;
}
