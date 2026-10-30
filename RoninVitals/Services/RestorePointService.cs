using System.Management;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Creates a System Restore point before a confirmation-required fix. Creating one needs administrator
/// rights, which the app already holds from launch; if it somehow does not, the exe relaunches itself
/// elevated just for this call.
/// </summary>
public static class RestorePointService
{
    public const string ElevatedArg = "--create-restore-point";

    // SystemRestore.CreateRestorePoint constants (root\default).
    private const uint BeginSystemChange = 100;
    private const uint ModifySettings = 12;

    // Exit codes shared by the in-process call and the elevated helper.
    private const int ExitCreated = 0;
    private const int ExitFailed = 1;
    private const int ExitSystemRestoreDisabled = 2;
    private const int ExitThrottled = 3;

    public static async Task<RestorePointResult> CreateRestorePointAsync(string description)
    {
        if (ElevationService.IsElevated)
            return MapExitCode(await Task.Run(() => RunElevatedCreateRestorePoint(description)));

        var launch = await Task.Run(() => ElevatedHelperLauncher.Run($"{ElevatedArg} \"{description.Replace("\"", "'")}\""));

        return launch.Outcome switch
        {
            ElevatedLaunchOutcome.Declined => new RestorePointResult(RestorePointOutcome.ElevationDeclined, "You declined the administrator prompt, so no restore point was created."),
            ElevatedLaunchOutcome.Failed => new RestorePointResult(RestorePointOutcome.Failed, $"Couldn't start the elevated helper: {launch.ErrorMessage}"),
            _ => MapExitCode(launch.ExitCode),
        };
    }

    private static RestorePointResult MapExitCode(int exitCode) => exitCode switch
    {
        ExitCreated => new RestorePointResult(RestorePointOutcome.Created, "A System Restore point was created."),
        ExitSystemRestoreDisabled => new RestorePointResult(RestorePointOutcome.SystemRestoreDisabled, "System Restore is turned off on this PC, so no restore point could be created."),
        ExitThrottled => new RestorePointResult(RestorePointOutcome.Throttled, "Windows only allows one new restore point per day, and one was already made recently."),
        _ => new RestorePointResult(RestorePointOutcome.Failed, "Windows couldn't create a restore point for an unknown reason."),
    };

    /// <summary>Does the WMI call and returns an exit code (see MapExitCode). Also run by the elevated helper from App.xaml.cs.</summary>
    public static int RunElevatedCreateRestorePoint(string description)
    {
        try
        {
            using var systemRestore = new ManagementClass(@"root\default", "SystemRestore", null);
            using var inParameters = systemRestore.GetMethodParameters("CreateRestorePoint");
            inParameters["Description"] = description;
            inParameters["RestorePointType"] = ModifySettings;
            inParameters["EventType"] = BeginSystemChange;

            using var result = systemRestore.InvokeMethod("CreateRestorePoint", inParameters, null);
            var returnValue = result is null ? 0u : Convert.ToUInt32(result["ReturnValue"]);

            return returnValue == 0 ? ExitCreated : ExitFailed;
        }
        catch (ManagementException exception) when (exception.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return ExitSystemRestoreDisabled;
        }
        catch (Exception)
        {
            return ExitFailed; // any WMI failure just means no restore point was made
        }
    }
}
