using System.ComponentModel;
using System.Diagnostics;

namespace RoninVitals.Services;

internal enum ElevatedLaunchOutcome { Finished, Declined, Failed }

internal sealed record ElevatedLaunchResult(ElevatedLaunchOutcome Outcome, int ExitCode = 0, string? ErrorMessage = null);

/// <summary>
/// Relaunches this exe with a UAC prompt to do one admin-only job and exit. Only needed when the app
/// is not already elevated; the manifest normally makes it so (see ElevationService).
/// </summary>
internal static class ElevatedHelperLauncher
{
    private const int ErrorCancelled = 1223; // the user clicked "No" on the UAC prompt

    /// <summary>Starts the helper with the given arguments and waits for its exit code.</summary>
    public static ElevatedLaunchResult Run(string arguments)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return new ElevatedLaunchResult(ElevatedLaunchOutcome.Failed, ErrorMessage: "Couldn't locate the app's own executable to relaunch elevated.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var helper = Process.Start(startInfo)!;
            helper.WaitForExit();
            return new ElevatedLaunchResult(ElevatedLaunchOutcome.Finished, helper.ExitCode);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            return new ElevatedLaunchResult(ElevatedLaunchOutcome.Declined);
        }
        catch (Exception exception)
        {
            return new ElevatedLaunchResult(ElevatedLaunchOutcome.Failed, ErrorMessage: exception.Message);
        }
    }
}
