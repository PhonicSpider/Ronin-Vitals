namespace RoninVitals.Models;

/// <summary>
/// Outcome of a real (non-scripted) check, computed at run time. TechCommand/TechDetail are
/// optional and only shown in the Tech Mode console - the real command or its closest PowerShell
/// equivalent (for checks that are in-process .NET/WMI calls with no literal command to point
/// at) plus the raw, unrounded reading behind ResultText's friendly phrasing. Without these, the
/// console just echoed the same sentence a regular user already sees on the checklist, which
/// isn't useful to a technician - see the owner's live feedback on the "slow" card.
/// </summary>
public sealed record CheckResult(CheckStatus Status, string ResultText, string? TechCommand = null, string? TechDetail = null);
