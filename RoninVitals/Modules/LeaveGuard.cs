namespace RoninVitals.Modules;

/// <summary>Asks the current screen whether it may be left. Screens that do not implement <see cref="ICanLeave"/> never block.</summary>
public static class LeaveGuard
{
    public static Task<bool> CanLeaveAsync(object? view) =>
        view is ICanLeave guard ? guard.CanLeaveAsync() : Task.FromResult(true);
}
