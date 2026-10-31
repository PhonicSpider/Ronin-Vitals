namespace RoninVitals.Models;

/// <summary>How a table row is drawn in the HTML report.</summary>
public enum ReportRowEmphasis
{
    Normal,

    /// <summary>Worth the reader's attention (an unusual port, a task that needs a second look).</summary>
    Notable,

    /// <summary>Expected and routine (a Windows default); shown but toned down.</summary>
    Muted,
}

public sealed record ReportRow(IReadOnlyList<string> Cells, ReportRowEmphasis Emphasis = ReportRowEmphasis.Normal);

/// <summary>
/// A list a check found, kept as data so the HTML report can lay it out as a real table. Cells may contain line breaks.
/// A <paramref name="StartsCollapsed"/> table is folded away until the reader opens it (for long, routine lists).
/// </summary>
public sealed record ReportTable(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ReportRow> Rows,
    string? Note = null,
    bool StartsCollapsed = false);
