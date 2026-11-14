namespace RoninVitals.Modules;

/// <summary>Implemented by a screen that needs to be asked before the app navigates away from it or closes.</summary>
public interface ICanLeave
{
    /// <summary>True when it is fine to leave the screen; false to stay (for example when the user chose to keep working).</summary>
    Task<bool> CanLeaveAsync();
}
