namespace RoninVitals.Models;

public enum RestorePointOutcome
{
    Created,
    SystemRestoreDisabled,
    Throttled,
    ElevationDeclined,
    Failed
}

public sealed record RestorePointResult(RestorePointOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome == RestorePointOutcome.Created;
}
