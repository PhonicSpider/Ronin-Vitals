using System.Management;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Drive-fragmentation check, HDD only: SSDs and NVMe drives don't fragment in a way that matters, so
/// they are answered without touching any fragmentation API. For an HDD the WMI DefragAnalysis method
/// needs administrator rights (tested against a real drive); the app already holds them from launch, and
/// if it does not, the exe relaunches itself elevated for just this call.
/// </summary>
public static partial class DriveFragmentationService
{
    public const string ElevatedArg = "--analyze-fragmentation";

    private const double FragmentedPercentThreshold = 10;

    // Exit codes shared by the in-process call and the elevated helper.
    private const int ExitNotFragmented = 0;
    private const int ExitFragmented = 1;
    private const int ExitUnknown = 2;

    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveLetterPattern();

    public static CheckResult CheckFragmentation()
    {
        var hddLetters = GetHddDriveLetters();
        if (hddLetters.Count == 0)
            return new CheckResult(CheckStatus.Pass, "All your drives are solid-state - fragmentation doesn't apply to SSDs/NVMe, nothing to optimize here.");

        // One representative HDD, so a non-elevated run shows a single UAC prompt rather than one per drive.
        var driveLetter = hddLetters[0];

        if (ElevationService.IsElevated)
            return MapExitCode(RunElevatedAnalyze(driveLetter), driveLetter);

        var launch = ElevatedHelperLauncher.Run($"{ElevatedArg} {driveLetter}");

        return launch.Outcome switch
        {
            ElevatedLaunchOutcome.Declined => new CheckResult(CheckStatus.Warn, $"You declined the administrator prompt, so fragmentation couldn't be checked on drive {driveLetter}."),
            ElevatedLaunchOutcome.Failed => new CheckResult(CheckStatus.Warn, $"Couldn't check fragmentation: {launch.ErrorMessage}"),
            _ => MapExitCode(launch.ExitCode, driveLetter),
        };
    }

    private static CheckResult MapExitCode(int exitCode, string driveLetter) => exitCode switch
    {
        ExitNotFragmented => new CheckResult(CheckStatus.Pass, $"Drive {driveLetter} isn't significantly fragmented."),
        ExitFragmented => new CheckResult(CheckStatus.Warn, $"Drive {driveLetter} is fragmented enough that defragmenting could help performance."),
        _ => new CheckResult(CheckStatus.Warn, $"Couldn't determine fragmentation on drive {driveLetter}."),
    };

    /// <summary>Analyzes one drive ("C:") and returns an exit code (see MapExitCode). Also run by the elevated helper from App.xaml.cs.</summary>
    public static int RunElevatedAnalyze(string driveLetter)
    {
        // The value goes into a WMI query, so accept only a bare drive letter.
        if (!DriveLetterPattern().IsMatch(driveLetter)) return ExitUnknown;

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Volume WHERE DriveLetter = '{driveLetter}'");
            using var volumes = searcher.Get();

            ManagementObject? volume = null;
            foreach (ManagementObject match in volumes) { volume = match; break; }
            if (volume is null) return ExitUnknown;

            using (volume)
            {
                using var analysis = volume.InvokeMethod("DefragAnalysis", null, null);
                if (analysis is null || Convert.ToUInt32(analysis["ReturnValue"]) != 0) return ExitUnknown;

                using var details = (ManagementBaseObject)analysis["DefragAnalysis"];
                var percentFragmented = details["TotalPercentFragmentation"];
                if (percentFragmented is null) return ExitUnknown;

                return Convert.ToDouble(percentFragmented) < FragmentedPercentThreshold ? ExitNotFragmented : ExitFragmented;
            }
        }
        catch (Exception)
        {
            return ExitUnknown; // WMI refused or failed; report "couldn't determine" rather than crash
        }
    }

    /// <summary>Drive letters that sit on a genuine HDD, from the shared drive inventory.</summary>
    private static List<string> GetHddDriveLetters() =>
        EnvironmentInfoService.GatherDriveInventory()
            .Where(drive => drive.MediaType == DriveMediaType.Hdd)
            .SelectMany(drive => drive.DriveLetters)
            .ToList();
}
