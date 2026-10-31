namespace RoninVitals.Models;

/// <summary>
/// What a check found. <paramref name="ResultText"/> is the plain-language line every user sees. <paramref name="TechCommand"/> and
/// <paramref name="TechDetail"/> are optional and only shown in Tech Mode: the command (or its closest PowerShell equivalent)
/// and the raw reading behind the friendly text. <paramref name="Tables"/> holds the full lists a check found (open ports,
/// scheduled tasks); the on-screen result stays short and the HTML report prints these in full.
/// </summary>
public sealed record CheckResult(
    CheckStatus Status,
    string ResultText,
    string? TechCommand = null,
    string? TechDetail = null,
    IReadOnlyList<ReportTable>? Tables = null);
