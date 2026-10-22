using RoninVitals.Models;

namespace RoninVitals.Tests;

public class ActionLogEntryTests
{
    private static ActionLogEntry MakeEntry(Func<Task<bool>>? undoAction = null, DateTime? timestamp = null, TimeSpan? undoWindow = null) => new()
    {
        Description = "Test fix",
        Timestamp = timestamp ?? DateTime.Now,
        Note = "Not needed for this fix.",
        UndoAction = undoAction,
        UndoWindow = undoWindow ?? TimeSpan.FromMinutes(15),
    };

    // ── CanUndo ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanUndo_TrueWhenUndoActionSetAndWithinWindow()
        => Assert.True(MakeEntry(undoAction: () => Task.FromResult(true)).CanUndo);

    [Fact]
    public void CanUndo_FalseWhenNoUndoAction()
        => Assert.False(MakeEntry(undoAction: null).CanUndo);

    [Fact]
    public void CanUndo_FalseAfterWindowExpires()
    {
        var entry = MakeEntry(
            undoAction: () => Task.FromResult(true),
            timestamp: DateTime.Now - TimeSpan.FromMinutes(20),
            undoWindow: TimeSpan.FromMinutes(15));

        Assert.False(entry.CanUndo);
    }

    [Fact]
    public void CanUndo_TrueJustInsideWindow()
    {
        var entry = MakeEntry(
            undoAction: () => Task.FromResult(true),
            timestamp: DateTime.Now - TimeSpan.FromMinutes(5),
            undoWindow: TimeSpan.FromMinutes(15));

        Assert.True(entry.CanUndo);
    }

    // ── UndoAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndoAsync_SetsIsUndoneOnSuccess()
    {
        var entry = MakeEntry(undoAction: () => Task.FromResult(true));

        var result = await entry.UndoAsync();

        Assert.True(result);
        Assert.True(entry.IsUndone);
        Assert.False(entry.UndoFailed);
        Assert.False(entry.CanUndo); // already undone - can't undo twice
    }

    [Fact]
    public async Task UndoAsync_SetsUndoFailedWhenActionReturnsFalse()
    {
        var entry = MakeEntry(undoAction: () => Task.FromResult(false));

        var result = await entry.UndoAsync();

        Assert.False(result);
        Assert.True(entry.UndoFailed);
        Assert.False(entry.IsUndone);
    }

    [Fact]
    public async Task UndoAsync_SetsUndoFailedWhenActionThrows()
    {
        var entry = MakeEntry(undoAction: () => throw new InvalidOperationException("boom"));

        var result = await entry.UndoAsync();

        Assert.False(result);
        Assert.True(entry.UndoFailed);
        Assert.False(entry.IsUndone);
    }

    [Fact]
    public async Task UndoAsync_ReturnsFalseWithoutThrowingWhenCannotUndo()
    {
        var entry = MakeEntry(undoAction: null);

        var result = await entry.UndoAsync();

        Assert.False(result);
        Assert.False(entry.UndoFailed);
        Assert.False(entry.IsUndone);
    }
}
