using System.Diagnostics;

namespace RoninVitals.Modules;

/// <summary>Asks the current screen whether it may be left. Screens that do not implement <see cref="ICanLeave"/> never block.</summary>
public static class LeaveGuard
{
    /// <summary>
    /// The screen's answer, or true when it has none. A screen that throws or is cancelled counts as "yes", so
    /// a broken screen can neither crash the app nor trap the user on it.
    /// </summary>
    public static async Task<bool> CanLeaveAsync(object? view)
    {
        if (view is not ICanLeave guard) return true;

        try
        {
            return await guard.CanLeaveAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"A screen failed when asked whether it could be left, so leaving was allowed: {ex.Message}");
            return true;
        }
    }
}
