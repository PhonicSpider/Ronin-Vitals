namespace RoninVitals.Models;

/// <summary>
/// Thrown by a fix that chose NOT to run because something has to happen first (for example "close your
/// browser first"). SafeFixService shows the message as-is instead of the generic "something went wrong",
/// and nothing is logged as applied, because nothing was.
/// </summary>
public sealed class FixDeclinedException : Exception
{
    public FixDeclinedException(string message) : base(message) { }
}
