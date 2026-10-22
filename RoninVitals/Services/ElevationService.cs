using System.Security.Principal;

namespace RoninVitals.Services;

/// <summary>
/// Whether the current process already holds an administrator token. The app manifest requests
/// elevation at launch (see app.manifest), so this is normally true for the whole app's lifetime -
/// used to skip the old per-action self-elevating relaunch (RestorePointService,
/// DriveFragmentationService) when there's nothing left to elevate to, avoiding a redundant
/// second UAC prompt for an action the user already approved once at startup.
/// </summary>
public static class ElevationService
{
    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
