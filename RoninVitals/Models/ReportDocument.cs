namespace RoninVitals.Models;

/// <summary>One line in the "about this PC" block at the top of a report.</summary>
public sealed record ReportFact(string Label, string Value);

/// <summary>One check as it appears in a report: what it found, what to do about it, and the detail behind it.</summary>
public sealed record ReportEntry(
    string Label,
    CheckStatus Status,
    string ResultText,
    string? Guidance,
    string? TechCommand,
    string? TechDetail,
    IReadOnlyList<ReportTable> Tables);

/// <summary>Everything needed to write a report. The same shape serves every card's report.</summary>
public sealed record ReportDocument(
    string Title,
    string Subtitle,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<ReportEntry> Entries,
    string FontFaceCss = "");
