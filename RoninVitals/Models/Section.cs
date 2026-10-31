namespace RoninVitals.Models;

public enum SectionKey
{
    Network,
    Hardware,
    Software,
    System,

    /// <summary>"Security checks" - reached from its own card on the home screen, not one
    /// of the four area tiles.</summary>
    Security
}

public sealed class Section
{
    public required SectionKey Key { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }

    /// <summary>The big question shown above the cards. Every troubleshooting area asks the same
    /// thing; the scan area is choosing a tool rather than describing a problem.</summary>
    public string Headline { get; init; } = "What seems to be the problem?";
    public required string AccentBrushKey { get; init; }
    public required string AccentDimBrushKey { get; init; }
    public required IReadOnlyList<Symptom> Symptoms { get; init; }
}
