namespace RoninVitals.Models;

public enum SectionKey
{
    Network,
    Hardware,
    Software,
    System
}

public sealed class Section
{
    public required SectionKey Key { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required string AccentBrushKey { get; init; }
    public required string AccentDimBrushKey { get; init; }
    public required IReadOnlyList<Symptom> Symptoms { get; init; }
}
