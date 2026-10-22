namespace RoninVitals.Models;

public sealed class Symptom
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public bool IsCatchAll { get; init; }
    public required IReadOnlyList<CheckDefinition> Checks { get; init; }
}
