using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly DispatcherTimer _vitalsTimer;
    private readonly List<VitalMetricViewModel> _allVitals;
    private readonly SystemMetricsService _metrics = new();

    public ShellViewModel()
    {
        // Stays collapsed until the first fix is applied, then stays visible for the rest of
        // the session (even past the undo window) so the user can always review what changed.
        LogEntries = ActionLogService.Entries;
        LogEntries.CollectionChanged += (_, _) => HasLogEntries = LogEntries.Count > 0;

        var network = new VitalMetricViewModel("Network", "Vitals.Blue.Brush", _metrics.SampleNetwork);
        var cpu = new VitalMetricViewModel("CPU", "Vitals.Teal.Brush", _metrics.SampleCpu);
        var ram = new VitalMetricViewModel("RAM", "Vitals.Yellow.Brush", _metrics.SampleRam);

        var driveNames = _metrics.GetFixedDriveNames();
        if (driveNames.Count == 0) driveNames = new List<string> { "C:" };
        var drives = driveNames
            .Select(name => new VitalMetricViewModel($"Disk {name}", "Vitals.Purple.Brush", () => _metrics.SampleDrive(name)))
            .ToList();

        DriveVitals = drives;
        HasMultipleDrives = drives.Count > 1;

        PrimaryVitals = HasMultipleDrives
            ? new List<VitalMetricViewModel> { network, cpu, ram }
            : new List<VitalMetricViewModel> { network, cpu, ram, drives[0] };

        _allVitals = new List<VitalMetricViewModel> { network, cpu, ram };
        _allVitals.AddRange(drives);

        _vitalsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _vitalsTimer.Tick += (_, _) =>
        {
            foreach (var vital in _allVitals) vital.Sample();
        };
        _vitalsTimer.Start();

        CurrentView = new HomeViewModel(NavigateToSection, NavigateToDrivers);
    }

    /// <summary>Network/CPU/RAM, plus the single drive inline when there's only one.</summary>
    public IReadOnlyList<VitalMetricViewModel> PrimaryVitals { get; }

    /// <summary>Every detected fixed drive. Only rendered as its own row when there's more than one.</summary>
    public IReadOnlyList<VitalMetricViewModel> DriveVitals { get; }

    public bool HasMultipleDrives { get; }

    public ObservableCollection<ActionLogEntry> LogEntries { get; }

    [ObservableProperty] private object _currentView;
    [ObservableProperty] private bool _hasLogEntries;

    [RelayCommand]
    private async Task UndoEntry(ActionLogEntry entry) => await entry.UndoAsync();

    private void NavigateHome() => CurrentView = new HomeViewModel(NavigateToSection, NavigateToDrivers);

    private void NavigateToSection(SectionKey key) => CurrentView = new SymptomsViewModel(key, NavigateHome);

    private async void NavigateToDrivers()
    {
        var vm = new DriversViewModel(NavigateHome);
        CurrentView = vm;
        await vm.StartScanAsync();
    }
}
