using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Modules;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly Action<SectionKey> _navigateToSection;
    private readonly Action _navigateToDrivers;
    private readonly Action<ModuleCard> _openModuleCard;

    public HomeViewModel(
        Action<SectionKey> navigateToSection,
        Action navigateToDrivers,
        IReadOnlyList<ModuleCard>? moduleCards = null,
        Action<ModuleCard>? openModuleCard = null)
    {
        _navigateToSection = navigateToSection;
        _navigateToDrivers = navigateToDrivers;
        _openModuleCard = openModuleCard ?? (_ => { });
        ModuleCards = moduleCards ?? [];
    }

    /// <summary>Cards supplied by optional modules, shown after the built-in tiles. Empty when no module is installed.</summary>
    public IReadOnlyList<ModuleCard> ModuleCards { get; }

    public bool HasModuleCards => ModuleCards.Count > 0;

    [ObservableProperty] private bool _restorePointInProgress;
    [ObservableProperty] private string _restorePointMessage = string.Empty;

    public bool HasRestorePointMessage => RestorePointMessage.Length > 0;

    partial void OnRestorePointMessageChanged(string value) => OnPropertyChanged(nameof(HasRestorePointMessage));

    [RelayCommand]
    private void OpenSection(SectionKey key) => _navigateToSection(key);

    [RelayCommand]
    private void OpenDrivers() => _navigateToDrivers();

    [RelayCommand]
    private void OpenModuleCard(ModuleCard card) => _openModuleCard(card);

    /// <summary>
    /// A standalone safety checkpoint, independent of any fix. Uses the same ConfirmationRequired path a risky fix would.
    /// </summary>
    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        RestorePointInProgress = true;
        RestorePointMessage = string.Empty;
        try
        {
            var outcome = await SafeFixService.ApplyAsync(
                "Manual restore point",
                FixTier.ConfirmationRequired,
                apply: () => Task.FromResult(true));

            RestorePointMessage = outcome.Message;
        }
        finally
        {
            RestorePointInProgress = false;
        }
    }
}
