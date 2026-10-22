using System.Diagnostics;
using System.Management;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Creates a System Restore point before a confirmation-required fix, per the brief's safety
/// architecture. Restore point creation needs administrator rights. The app now requests
/// elevation at launch (see app.manifest), so the common path just runs the WMI call in-process
/// (offloaded via Task.Run since it's a blocking call). The relaunch-with-a-fresh-UAC-prompt
/// path below is kept as a fallback for the case where the process somehow isn't elevated
/// (e.g. the manifest not being honored under an unusual launch method) - "runas" always shows
/// its own consent prompt even from an already-elevated caller, so skipping it when possible
/// avoids asking the user to approve the same thing twice.
/// </summary>
public static class RestorePointService
{
    public const string ElevatedArg = "--create-restore-point";

    // MSFT SystemRestore.CreateRestorePoint EventType/RestorePointType constants (root\default).
    private const uint BeginSystemChange = 100;
    private const uint ModifySettings = 12;

    public static async Task<RestorePointResult> CreateRestorePointAsync(string description)
    {
        if (ElevationService.IsElevated)
            return MapExitCode(await Task.Run(() => RunElevatedCreateRestorePoint(description)));

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return new RestorePointResult(RestorePointOutcome.Failed, "Couldn't locate the app's own executable to relaunch elevated.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{ElevatedArg} \"{description.Replace("\"", "'")}\"",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED - user clicked "No"
        {
            return new RestorePointResult(RestorePointOutcome.ElevationDeclined, "You declined the administrator prompt, so no restore point was created.");
        }
        catch (Exception ex)
        {
            return new RestorePointResult(RestorePointOutcome.Failed, $"Couldn't start the elevated helper: {ex.Message}");
        }

        await process.WaitForExitAsync();

        return MapExitCode(process.ExitCode);
    }

    private static RestorePointResult MapExitCode(int exitCode) => exitCode switch
    {
        0 => new RestorePointResult(RestorePointOutcome.Created, "A System Restore point was created."),
        2 => new RestorePointResult(RestorePointOutcome.SystemRestoreDisabled, "System Restore is turned off on this PC, so no restore point could be created."),
        3 => new RestorePointResult(RestorePointOutcome.Throttled, "Windows only allows one new restore point per day, and one was already made recently."),
        _ => new RestorePointResult(RestorePointOutcome.Failed, "Windows couldn't create a restore point for an unknown reason."),
    };

    /// <summary>
    /// Runs in the elevated child process only - invoked from App.xaml.cs before any UI is
    /// created. Returns a process exit code (see CreateRestorePointAsync's mapping above).
    /// </summary>
    public static int RunElevatedCreateRestorePoint(string description)
    {
        try
        {
            using var systemRestore = new ManagementClass(@"root\default", "SystemRestore", null);
            using var inParams = systemRestore.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = ModifySettings;
            inParams["EventType"] = BeginSystemChange;

            using var result = systemRestore.InvokeMethod("CreateRestorePoint", inParams, null);
            var returnValue = result is null ? 0u : Convert.ToUInt32(result["ReturnValue"]);

            // 0 = success. Common non-zero values: 0x80070191 (feature disabled) surfaces as a
            // ManagementException below rather than a return value in practice, but check anyway.
            return returnValue == 0 ? 0 : 1;
        }
        catch (ManagementException ex) when (ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        catch (ManagementException)
        {
            return 1;
        }
        catch
        {
            return 1;
        }
    }
}
