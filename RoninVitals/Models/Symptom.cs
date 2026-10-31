namespace RoninVitals.Models;

public sealed class Symptom
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public bool IsCatchAll { get; init; }

    /// <summary>When set, the card offers an HTML report of its results after a run, under this title (for example "Security report").</summary>
    public string? ReportTitle { get; init; }
    public required IReadOnlyList<CheckDefinition> Checks { get; init; }
}
