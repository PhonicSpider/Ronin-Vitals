namespace RoninVitals.Modules;

/// <summary>
/// Implemented by a screen that needs to be asked before the app navigates away from it or closes.
/// The answer must always arrive: a task that never completes cannot be bounded by the shell, because a
/// timeout would answer for a user who is still reading a prompt. The method should not throw; if it does,
/// the shell treats that as "yes" so the user is never trapped on a broken screen.
/// </summary>
public interface ICanLeave
{
    /// <summary>True when it is fine to leave the screen; false to stay (for example when the user chose to keep working).</summary>
    Task<bool> CanLeaveAsync();
}
