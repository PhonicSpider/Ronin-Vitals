using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class DriversViewModel : ObservableObject
{
    private static readonly (int Pct, string Label)[] LoadingSteps =
    {
        (22, "Reading installed devices…"),
        (48, "Checking Windows Update catalog…"),
        (70, "Checking manufacturer catalogs…"),
        (100, "Comparing versions…"),
    };

    private readonly Action _navigateHome;
    private readonly List<DriverRowViewModel> _allRows = new();

    public DriversViewModel(Action navigateHome)
    {
        _navigateHome = navigateHome;
    }

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private double _loadingProgress;
    [ObservableProperty] private string _loadingLabel = "Reading installed devices…";
    [ObservableProperty] private string _headline = "Scanning your system…";
    [ObservableProperty] private int _updateCount;
    [ObservableProperty] private int _vendorCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isUpdating;

    public string UpdateButtonLabel => IsUpdating
        ? "Updating…"
        : SelectedCount > 0 ? $"Update Selected ({SelectedCount})" : "Update Selected";

    public string SelectedCountText => SelectedCount == 0 ? "0 selected" : $"{SelectedCount} selected";
    public bool CanUpdateSelected => SelectedCount > 0 && !IsUpdating;

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(UpdateButtonLabel));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanUpdateSelected));
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(UpdateButtonLabel));
        OnPropertyChanged(nameof(CanUpdateSelected));
    }

    public ObservableCollection<VendorGroupViewModel> VendorGroups { get; } = new();

    [RelayCommand]
    private void Back() => _navigateHome();

    public async Task StartScanAsync()
    {
        IsLoading = true;
        Headline = "Scanning your system…";
        LoadingProgress = 0;
        VendorGroups.Clear();
        _allRows.Clear();

        foreach (var (pct, label) in LoadingSteps)
        {
            await Task.Delay(550);
            LoadingProgress = pct;
            LoadingLabel = label;
        }

        await Task.Delay(300);
        BuildResults();
        IsLoading = false;
        Headline = "Here's what we found";
    }

    private void BuildResults()
    {
        var drivers = VitalsCatalog.Drivers;
        UpdateCount = drivers.Count;
        VendorCount = drivers.Select(d => d.Vendor).Distinct().Count();

        foreach (var vendor in drivers.Select(d => d.Vendor).Distinct())
        {
            var rows = new ObservableCollection<DriverRowViewModel>(
                drivers.Where(d => d.Vendor == vendor)
                       .Select(d => new DriverRowViewModel(d, UpdateSelectedCount)));
            foreach (var row in rows) _allRows.Add(row);
            VendorGroups.Add(new VendorGroupViewModel { VendorName = vendor, Rows = rows });
        }
    }

    private void UpdateSelectedCount() => SelectedCount = _allRows.Count(r => r.IsChecked);

    [RelayCommand]
    private async Task UpdateSelectedAsync()
    {
        if (SelectedCount == 0 || IsUpdating) return;
        IsUpdating = true;
        await Task.Delay(1400);
        foreach (var row in _allRows.Where(r => r.IsChecked))
        {
            row.MarkUpdated();
        }
        await Task.Delay(900);
        IsUpdating = false;
        UpdateSelectedCount();
    }
}
