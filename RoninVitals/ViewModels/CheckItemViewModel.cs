using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

public partial class CheckItemViewModel : ObservableObject
{
    private readonly CheckDefinition _definition;

    public CheckItemViewModel(CheckDefinition definition)
    {
        _definition = definition;
    }

    public string Label => _definition.Label;
    public bool HasFix => _definition.FixLabel is not null;
    public string FixLabel => _definition.FixLabel ?? string.Empty;
    public bool ShowFixArea => HasFix && ShowResult && Status != CheckStatus.Pass;

    /// <summary>Whether this entry actually changes something (ApplyFix set) vs. just shows
    /// static advice (FixInfoText only, e.g. a router setting Vitals can't touch). Drives the
    /// post-click button label - "Applied" is wrong/misleading for an entry that never applied
    /// anything.</summary>
    public bool IsRealFix => _definition.ApplyFix is not null;

    [ObservableProperty] private CheckStatus _status = CheckStatus.Pending;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private bool _showResult;
    [ObservableProperty] private bool _fixApplied;
    [ObservableProperty] private bool _fixInProgress;
    [ObservableProperty] private string _fixMessage = string.Empty;

    /// <summary>The real command (or its closest PowerShell equivalent, for in-process .NET/WMI
    /// checks) and raw reading behind ResultText - only populated for checks that set them on
    /// their CheckResult, used by the Tech Mode console instead of repeating the friendly text a
    /// regular user already sees. Null for scripted checks and any real check that hasn't been
    /// given technical detail yet.</summary>
    public string? TechCommand { get; private set; }
    public string? TechDetail { get; private set; }

    /// <summary>The full lists the check found (open ports, scheduled tasks), kept for the HTML report; the on-screen result stays short.</summary>
    public IReadOnlyList<ReportTable> Tables { get; private set; } = [];

    /// <summary>This check as it should read in a report. Guidance is only offered when something needs attention.</summary>
    public ReportEntry ToReportEntry() =>
        new(Label, Status, ResultText, Status is CheckStatus.Warn or CheckStatus.Bad ? _definition.FixInfoText : null, TechCommand, TechDetail, Tables);

    public bool HasFixMessage => FixMessage.Length > 0;

    partial void OnStatusChanged(CheckStatus value) => OnPropertyChanged(nameof(ShowFixArea));
    partial void OnShowResultChanged(bool value) => OnPropertyChanged(nameof(ShowFixArea));
    partial void OnFixMessageChanged(string value) => OnPropertyChanged(nameof(HasFixMessage));

    public void Reset()
    {
        Status = CheckStatus.Pending;
        ResultText = string.Empty;
        ShowResult = false;
        FixApplied = false;
        FixMessage = string.Empty; // otherwise guidance opened on a previous run stays showing after "Run again"
        Tables = [];
    }

    public async Task CompleteAsync()
    {
        if (_definition.RealCheck is not null)
        {
            var result = await Task.Run(_definition.RealCheck);
            Status = result.Status;
            ResultText = result.ResultText;
            TechCommand = result.TechCommand;
            TechDetail = result.TechDetail;
            Tables = result.Tables ?? [];
        }
        else
        {
            Status = _definition.ScriptedStatus;
            ResultText = _definition.ResultText;
            TechCommand = null;
            TechDetail = null;
            Tables = [];
        }
        ShowResult = true;
    }

    [RelayCommand]
    private async Task ApplyFix()
    {
        if (_definition.ApplyFix is null)
        {
            FixApplied = true;
            FixMessage = _definition.FixInfoText ?? string.Empty;
            return;
        }

        FixInProgress = true;
        FixMessage = string.Empty;
        try
        {
            var outcome = await SafeFixService.ApplyAsync(
                _definition.FixLabel ?? _definition.Label,
                _definition.Tier,
                _definition.ApplyFix,
                _definition.UndoFix);

            FixApplied = outcome.Succeeded;
            FixMessage = outcome.Message;
        }
        finally
        {
            FixInProgress = false;
        }
    }
}
