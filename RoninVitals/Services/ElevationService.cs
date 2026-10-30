using System.Security.Principal;

namespace RoninVitals.Services;

/// <summary>
/// Whether the current process already holds an administrator token. The manifest requests elevation
/// at launch, so this is normally true; admin-only work then runs in-process instead of relaunching
/// through ElevatedHelperLauncher and asking for a second UAC approval.
/// </summary>
public static class ElevationService
{
    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
}
