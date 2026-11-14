namespace RoninVitals.Modules;

/// <summary>What the app hands to a module when it is initialized.</summary>
public sealed class ModuleContext
{
    /// <summary>Returns to the app's home screen.</summary>
    public required Action NavigateHome { get; init; }
}
