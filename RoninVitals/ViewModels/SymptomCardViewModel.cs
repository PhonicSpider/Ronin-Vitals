using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class SymptomCardViewModel : ObservableObject
{
    private readonly Symptom _symptom;
    private readonly Action<SymptomCardViewModel> _requestExpand;
    private readonly Action _requestCollapse;

    public SymptomCardViewModel(Symptom symptom, Action<SymptomCardViewModel> requestExpand, Action requestCollapse)
    {
        _symptom = symptom;
        _requestExpand = requestExpand;
        _requestCollapse = requestCollapse;
        Checks = new ObservableCollection<CheckItemViewModel>(
            symptom.Checks.Select(check => new CheckItemViewModel(check)));
    }

    public string Id => _symptom.Id;
    public string Title => _symptom.Title;
    public string Subtitle => _symptom.Subtitle;
    public bool IsCatchAll => _symptom.IsCatchAll;
    public int CheckCount => _symptom.Checks.Count;

    public ObservableCollection<CheckItemViewModel> Checks { get; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSiblingCollapsed;
    [ObservableProperty] private bool _isGatheringInfo;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasRunOnce;
    [ObservableProperty] private bool _showSummary;
    [ObservableProperty] private bool _summaryHasIssues;
    [ObservableProperty] private string _summaryText = string.Empty;

    /// <summary>Only non-null once a diagnostic run has started with Tech Mode on - see
    /// RunDiagnosticAsync. Not recreated on every run; the same console (and its underlying
    /// shell process, once the technician has used it) persists across "Run again" within one
    /// card expansion, only torn down when the card collapses (SymptomsViewModel.CollapseExpanded).</summary>
    public TechConsoleViewModel? Console { get; private set; }

    [ObservableProperty] private bool _isTechConsoleVisible;

    /// <summary>How long after a diagnostic run starts before the Tech Mode console zooms in.</summary>
    public static TimeSpan TechConsoleRevealDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Set by the "Gathering information..." pre-flight step (see GatherInformationAsync)
    /// - re-verified drive/partition data for this machine, for future checks to draw on.</summary>
    public IReadOnlyList<DriveInventoryEntry> DriveInventory { get; private set; } = Array.Empty<DriveInventoryEntry>();

    /// <summary>The pacing floor for GatherInformationAsync - a mutable static (not a hardcoded
    /// constant) so tests can zero it out. Without this, RoninVitals.Tests' SymptomsViewModelTests/
    /// SymptomCardViewModelTests (which call ExpandCommand) each pay a real 10s wait, since the
    /// gathering task is real production code, not a mock - that alone took the suite from ~1s to
    /// ~40s the first time this was wired in.</summary>
    public static TimeSpan GatheringMinimumDuration { get; set; } = TimeSpan.FromSeconds(10);

    public string RunButtonLabel => IsRunning ? "Checking…" : HasRunOnce ? "Run again" : "Run diagnostic";

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(RunButtonLabel));
    partial void OnHasRunOnceChanged(bool value) => OnPropertyChanged(nameof(RunButtonLabel));

    [RelayCommand]
    private void Expand()
    {
        if (IsExpanded) return;
        _requestExpand(this);
    }

    [RelayCommand]
    private void Collapse() => _requestCollapse();

    public void ResetRun()
    {
        IsRunning = false;
        HasRunOnce = false;
        ShowSummary = false;
        IsTechConsoleVisible = false;
        Console?.Clear();
        foreach (var check in Checks) check.Reset();
    }

    /// <summary>Tears down the console's shell process (if the technician ever used one) - called
    /// from SymptomsViewModel.CollapseExpanded so a PowerShell process doesn't keep running for
    /// every card the user has ever expanded in a session.</summary>
    public void DisposeConsole()
    {
        Console?.Dispose();
        Console = null;
    }

    /// <summary>
    /// Real background work for the "Gathering information..." pre-flight step shown once per
    /// card expand, paced to a ~10s minimum so it reads as substantive rather than instant (the
    /// underlying WMI queries usually finish in well under a second on their own). The caller
    /// (SymptomsViewModel.ExpandCard) sets IsGatheringInfo=true before flipping IsExpanded, so
    /// the initial expand animation measures the small loading view instead of the full
    /// checklist; this flips IsGatheringInfo back off once both the real work and the floor are
    /// done, revealing the checklist underneath.
    /// </summary>
    public async Task GatherInformationAsync()
    {
        try
        {
            var minimumWait = Task.Delay(GatheringMinimumDuration);
            var inventoryTask = EnvironmentInfoService.GatherDriveInventoryAsync();
            await Task.WhenAll(minimumWait, inventoryTask);
            DriveInventory = inventoryTask.Result;
        }
        finally
        {
            IsGatheringInfo = false;
        }
    }

    [RelayCommand]
    private async Task RunDiagnosticAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        ShowSummary = false;
        foreach (var check in Checks) check.Reset();

        if (TechModeService.Instance.IsEnabled)
        {
            Console ??= new TechConsoleViewModel();
            OnPropertyChanged(nameof(Console));
            Console.Clear();
            _ = RevealTechConsoleAsync();
        }

        var issues = 0;
        foreach (var check in Checks)
        {
            check.Status = CheckStatus.Running;
            await Task.Delay(550);
            await check.CompleteAsync();
            if (check.Status != CheckStatus.Pass) issues++;
            Console?.AppendSystemLine(check.TechCommand ?? check.Label, check.TechDetail ?? check.ResultText);
        }

        HasRunOnce = true;
        IsRunning = false;
        SummaryHasIssues = issues > 0;
        SummaryText = issues == 0
            ? "Everything checks out — no issues found here."
            : $"Found {issues} thing{(issues > 1 ? "s" : "")} worth fixing above.";
        ShowSummary = true;
    }

    /// <summary>Fire-and-forget from RunDiagnosticAsync - deliberately not awaited inline, so the
    /// checklist's own pacing/completion is never blocked by this purely cosmetic delay.</summary>
    private async Task RevealTechConsoleAsync()
    {
        await Task.Delay(TechConsoleRevealDelay);
        IsTechConsoleVisible = true;
    }
}
