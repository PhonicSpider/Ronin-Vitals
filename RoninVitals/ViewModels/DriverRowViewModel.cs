using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;

namespace RoninVitals.ViewModels;

public partial class DriverRowViewModel : ObservableObject
{
    private readonly DriverEntry _entry;
    private readonly Action _onSelectionChanged;

    public DriverRowViewModel(DriverEntry entry, Action onSelectionChanged)
    {
        _entry = entry;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Name => _entry.Name;
    public string CurrentVersion => _entry.CurrentVersion;
    public string LatestVersion => _entry.LatestVersion;
    public bool IsSelectable => _entry.Method != DriverUpdateMethod.Manual;
    public bool IsLinkOut => _entry.Method == DriverUpdateMethod.Manual;

    public string BadgeText => _entry.Method switch
    {
        DriverUpdateMethod.WindowsUpdate => "Windows Update",
        DriverUpdateMethod.Direct => "Direct",
        _ => "Manual",
    };

    public string BadgeBrushKey => _entry.Method switch
    {
        DriverUpdateMethod.WindowsUpdate => "Vitals.Blue.Brush",
        DriverUpdateMethod.Direct => "Vitals.Teal.Brush",
        _ => "Vitals.Yellow.Brush",
    };

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isUpdated;
    [ObservableProperty] private string _linkOutState = "View Update";

    partial void OnIsCheckedChanged(bool value) => _onSelectionChanged();

    [RelayCommand]
    private void ToggleSelected()
    {
        if (!IsSelectable) return;
        IsChecked = !IsChecked;
    }

    [RelayCommand]
    private async Task OpenVendorPageAsync()
    {
        LinkOutState = "Opening…";
        await Task.Delay(900);
        LinkOutState = "View Update";
    }

    public void MarkUpdated()
    {
        IsUpdated = true;
        IsChecked = false;
    }
}
