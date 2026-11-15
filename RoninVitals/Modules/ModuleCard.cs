namespace RoninVitals.Modules;

/// <summary>A home-screen card that a module contributes, and how to open the screen behind it.</summary>
/// <param name="Title">The card heading.</param>
/// <param name="Subtitle">One line under the heading saying what the card does.</param>
/// <param name="IconPathData">WPF path mini-language data for the card icon.</param>
/// <param name="CreateScreen">Builds the screen to show when the card is opened.</param>
public sealed record ModuleCard(string Title, string Subtitle, string IconPathData, Func<object> CreateScreen)
{
    /// <summary>The screen for this card, or null when the module's factory fails, so one broken module cannot crash the app.</summary>
    public object? TryCreateScreen()
    {
        try
        {
            return CreateScreen();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
