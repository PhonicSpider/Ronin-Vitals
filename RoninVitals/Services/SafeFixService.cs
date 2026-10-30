using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// The single path every real fix goes through - ties restore-point creation, the fix itself,
/// and the action log together per the brief's safety architecture. A confirmation-required
/// fix that can't get a restore point does not run: refusing to act beats acting without a
/// safety net. Safe-auto-fix tier skips the restore point entirely (already whitelisted as
/// reversible/harmless on their own).
/// </summary>
public static class SafeFixService
{
    public static async Task<FixOutcome> ApplyAsync(
        string description,
        FixTier tier,
        Func<Task<bool>> apply,
        Func<Task<bool>>? undo = null)
    {
        var restorePointNote = "Not needed for this fix.";

        if (tier == FixTier.ConfirmationRequired)
        {
            var restorePoint = await RestorePointService.CreateRestorePointAsync(description);
            restorePointNote = restorePoint.Message;

            if (!restorePoint.Succeeded)
            {
                return new FixOutcome(false,
                    $"Didn't make this change - couldn't create a safety checkpoint first. {restorePoint.Message}");
            }
        }

        bool applied;
        try
        {
            applied = await apply();
        }
        catch (FixDeclinedException exception)
        {
            return new FixOutcome(false, exception.Message);
        }
        catch (Exception exception)
        {
            return new FixOutcome(false, $"Something went wrong applying the fix: {exception.Message}");
        }

        if (!applied)
            return new FixOutcome(false, "The fix didn't complete successfully.");

        ActionLogService.Record(new ActionLogEntry
        {
            Description = description,
            Timestamp = DateTime.Now,
            Note = restorePointNote,
            UndoAction = undo,
        });

        return new FixOutcome(true, "Done.");
    }
}
