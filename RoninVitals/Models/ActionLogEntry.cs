using CommunityToolkit.Mvvm.ComponentModel;

namespace RoninVitals.Models;

/// <summary>
/// One applied fix, recorded in plain language with enough state to offer a time-limited
/// undo. See Services/ActionLogService and Services/SafeFixService. Observable so the action
/// log panel's Undo button can react to IsUndone/UndoFailed without a manual UI refresh.
/// </summary>
public sealed partial class ActionLogEntry : ObservableObject
{
    public required string Description { get; init; }
    public required DateTime Timestamp { get; init; }
    /// <summary>Secondary detail line shown under the description - a restore-point confirmation
    /// for a fix entry, or command output for a tech-console entry.</summary>
    public required string Note { get; init; }
    public Func<Task<bool>>? UndoAction { get; init; }
    public TimeSpan UndoWindow { get; init; } = TimeSpan.FromMinutes(15);

    [ObservableProperty] private bool _isUndone;
    [ObservableProperty] private bool _undoFailed;

    public bool CanUndo => UndoAction is not null && !IsUndone && DateTime.Now - Timestamp < UndoWindow;

    partial void OnIsUndoneChanged(bool value) => OnPropertyChanged(nameof(CanUndo));

    public async Task<bool> UndoAsync()
    {
        if (!CanUndo) return false;

        try
        {
            var undoSucceeded = await UndoAction!();
            if (undoSucceeded) IsUndone = true;
            else UndoFailed = true;
            return undoSucceeded;
        }
        catch
        {
            UndoFailed = true;
            return false;
        }
    }
}
