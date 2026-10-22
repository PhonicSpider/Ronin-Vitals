namespace RoninVitals.Models;

/// <summary>
/// Description of a single diagnostic check. When <see cref="RealCheck"/> is set, it is invoked
/// at run time and its result is used instead of <see cref="ScriptedStatus"/>/<see cref="ResultText"/>,
/// which remain as the scripted/demo fallback ported from the HTML prototype. See the build
/// checklist (CHECKLIST.md) for which checks are wired to real data vs still scripted.
/// </summary>
public sealed class CheckDefinition
{
    public required string Label { get; init; }
    public required CheckStatus ScriptedStatus { get; init; }
    public required string ResultText { get; init; }
    public string? FixLabel { get; init; }

    /// <summary>
    /// Static advice shown under the fix button when clicked, for conditions Vitals can't act on
    /// directly (a router's Wi-Fi channel, physical adapter placement, etc.) - only used when
    /// <see cref="ApplyFix"/> is null. Without this, clicking an informational fix button just
    /// flips to "Applied" with nothing to show for it, which reads as broken.
    /// </summary>
    public string? FixInfoText { get; init; }

    /// <summary>Runs on a background thread when set - keep it read-only and fast. See DriveHealthService for an example.</summary>
    public Func<CheckResult>? RealCheck { get; init; }

    /// <summary>
    /// Safety tier for <see cref="ApplyFix"/> - ignored unless ApplyFix is set. SafeAutoFix skips
    /// the restore point; ConfirmationRequired always gets one first via SafeFixService.
    /// </summary>
    public FixTier Tier { get; init; } = FixTier.SafeAutoFix;

    /// <summary>The real fix action, run through SafeFixService when set. Null keeps the old cosmetic "Applied" behavior.</summary>
    public Func<Task<bool>>? ApplyFix { get; init; }

    /// <summary>Reverses ApplyFix, offered for the entry's undo window in the action log. Optional even when ApplyFix is set.</summary>
    public Func<Task<bool>>? UndoFix { get; init; }
}
