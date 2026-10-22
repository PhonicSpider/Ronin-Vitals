namespace RoninVitals.Models;

/// <summary>
/// Matches the brief's safety-architecture tiers. SafeAutoFix = whitelisted, no confirmation
/// or restore point needed (DNS flush, adapter reset, temp file cleanup, restart a hung
/// service). ConfirmationRequired = anything with side effects beyond the immediate issue
/// (registry edits, driver installs/rollback) - always gets a System Restore point first, and
/// the fix does not run if that restore point can't be created.
/// </summary>
public enum FixTier
{
    SafeAutoFix,
    ConfirmationRequired
}
