using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Modules;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly DispatcherTimer _vitalsTimer;
    private readonly DispatcherTimer _uptimeTimer;
    private readonly List<VitalMetricViewModel> _allVitals;
    private readonly SystemMetricsService _metrics = new();
    private readonly IReadOnlyList<ModuleCard> _moduleCards;

    public ShellViewModel(IReadOnlyList<IFeatureModule>? discovered = null)
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
        UptimeText = _metrics.SampleUptimeText();
        _vitalsTimer.Tick += (_, _) =>
        {
            foreach (var vital in _allVitals) vital.Sample();
        };
        _vitalsTimer.Start();

        // Its own faster timer: the clock shows seconds, and sampling it on the 700 ms vitals timer
        // would repeat one second and skip the next. 250 ms keeps it ticking evenly, and reading the
        // tick counter is effectively free.
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uptimeTimer.Tick += (_, _) => UptimeText = _metrics.SampleUptimeText();
        _uptimeTimer.Start();

        _moduleCards = InitializeModules(discovered ?? ModuleHost.Discovered);
        CurrentView = CreateHome();
    }

    /// <summary>Network/CPU/RAM, plus the single drive inline when there's only one.</summary>
    public IReadOnlyList<VitalMetricViewModel> PrimaryVitals { get; }

    /// <summary>Every detected fixed drive. Only rendered as its own row when there's more than one.</summary>
    public IReadOnlyList<VitalMetricViewModel> DriveVitals { get; }

    public bool HasMultipleDrives { get; }

    public ObservableCollection<ActionLogEntry> LogEntries { get; }

    [ObservableProperty] private object _currentView;
    [ObservableProperty] private bool _hasLogEntries;

    /// <summary>The report window drawn over the whole app while a report is being looked at; null when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReportClosed))]
    private ReportPreviewViewModel? _reportPreview;

    /// <summary>Lets the screen behind the report window be switched off while the report is open.</summary>
    public bool IsReportClosed => ReportPreview is null;

    /// <summary>System uptime as "Days:Hours:Minutes:Seconds", shown as floating text in the top bar (not
    /// a vitals tile - it has no gauge). Refreshed by its own timer so the seconds tick evenly.</summary>
    [ObservableProperty] private string _uptimeText = string.Empty;

    [RelayCommand]
    private async Task UndoEntry(ActionLogEntry entry) => await entry.UndoAsync();

    /// <summary>Asks the current screen whether the window may close.</summary>
    public Task<bool> CanCloseAsync() => LeaveGuard.CanLeaveAsync(CurrentView);

    private async void NavigateHome()
    {
        if (!await LeaveGuard.CanLeaveAsync(CurrentView)) return;
        CurrentView = CreateHome();
    }

    private HomeViewModel CreateHome() => new(NavigateToSection, NavigateToDrivers, _moduleCards, OpenModuleCard);

    /// <summary>Initializes the discovered modules and gathers their cards in module order. Modules that fail are skipped.</summary>
    private IReadOnlyList<ModuleCard> InitializeModules(IReadOnlyList<IFeatureModule> discovered)
    {
        if (discovered.Count == 0) return [];

        var context = new ModuleContext { NavigateHome = NavigateHome };
        var result = ModuleLoader.Initialize(discovered, context);
        foreach (var problem in result.Problems)
        {
            Trace.TraceWarning(problem);
        }
        return result.Modules.SelectMany(module => module.Cards).ToList();
    }

    private async void OpenModuleCard(ModuleCard card)
    {
        if (!await LeaveGuard.CanLeaveAsync(CurrentView)) return;

        var screen = card.TryCreateScreen();
        if (screen is null)
        {
            Trace.TraceWarning($"The screen for the card \"{card.Title}\" could not be opened, so the current screen was kept.");
            return;
        }
        CurrentView = screen;
    }

    private async void NavigateToSection(SectionKey key)
    {
        if (!await LeaveGuard.CanLeaveAsync(CurrentView)) return;
        CurrentView = new SymptomsViewModel(key, NavigateHome, ShowReport);
    }

    private void ShowReport(ReportPreviewViewModel preview)
    {
        preview.CloseRequested += (_, _) => ReportPreview = null;
        ReportPreview = preview;
    }

    private async void NavigateToDrivers()
    {
        if (!await LeaveGuard.CanLeaveAsync(CurrentView)) return;
        var driversViewModel = new DriversViewModel(NavigateHome);
        CurrentView = driversViewModel;
        await driversViewModel.StartScanAsync();
    }
}
