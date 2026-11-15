using System.Windows;

namespace RoninVitals.Modules;

/// <summary>An optional add-on that can be dropped next to the app to contribute extra cards to the home screen.</summary>
public interface IFeatureModule
{
    /// <summary>False when the module is present but should stay hidden, so the app behaves as if it were not installed.</summary>
    bool IsAvailable { get; }

    /// <summary>The cards this module adds to the home screen, in display order.</summary>
    IReadOnlyList<ModuleCard> Cards { get; }

    /// <summary>Styles and templates the module needs merged into the application, or null when it has none.</summary>
    ResourceDictionary? Resources { get; }

    /// <summary>Called once after loading, before the module's cards are shown.</summary>
    void Initialize(ModuleContext context);
}
