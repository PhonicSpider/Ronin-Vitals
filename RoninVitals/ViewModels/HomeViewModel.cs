using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly Action<SectionKey> _navigateToSection;
    private readonly Action _navigateToDrivers;

    public HomeViewModel(Action<SectionKey> navigateToSection, Action navigateToDrivers)
    {
        _navigateToSection = navigateToSection;
        _navigateToDrivers = navigateToDrivers;
    }

    [ObservableProperty] private bool _restorePointInProgress;
    [ObservableProperty] private string _restorePointMessage = string.Empty;

    public bool HasRestorePointMessage => RestorePointMessage.Length > 0;

    partial void OnRestorePointMessageChanged(string value) => OnPropertyChanged(nameof(HasRestorePointMessage));

    [RelayCommand]
    private void OpenSection(SectionKey key) => _navigateToSection(key);

    [RelayCommand]
    private void OpenDrivers() => _navigateToDrivers();

    /// <summary>
    /// A standalone safety checkpoint, independent of any specific fix - the same pattern
    /// utilities like Disk Cleanup use ("create a restore point before cleaning"). Goes through
    /// the same ConfirmationRequired path any future fix would, so it's the one place this
    /// session's elevation/UAC flow can be verified live without inventing a real system change.
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
