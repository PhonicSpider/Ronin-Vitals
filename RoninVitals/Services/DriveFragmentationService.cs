using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real drive-fragmentation check, HDD-only. SSD/NVMe drives don't fragment in any way that
/// matters (no moving parts, no seek-time penalty, and Windows runs TRIM instead of defrag on
/// them) - those are answered instantly and honestly without touching any fragmentation API at
/// all. For a genuine HDD, both `defrag /A` (analyze-only) and the legacy
/// `Win32_Volume.DefragAnalysis` WMI method were tested against a real HDD on the dev machine
/// and both require admin rights just to read fragmentation on this Windows version - `defrag /A`
/// refused outright ("insufficient privileges"), and the WMI method returned an empty/failed
/// result (ReturnValue 11, every field blank) without elevation. The app requests elevation at
/// launch (see app.manifest), so this normally just runs the analysis in-process; the
/// relaunch-with-"runas" path below is kept as a fallback for the rare case the process isn't
/// already elevated, same reasoning as RestorePointService.
/// </summary>
public static class DriveFragmentationService
{
    public const string ElevatedArg = "--analyze-fragmentation";

    public static CheckResult CheckFragmentation()
    {
        var hddLetters = GetHddDriveLetters();
        if (hddLetters.Count == 0)
            return new CheckResult(CheckStatus.Pass, "All your drives are solid-state - fragmentation doesn't apply to SSDs/NVMe, nothing to optimize here.");

        // Analyze one representative HDD rather than elevating once per drive - multiple UAC
        // prompts in a single diagnostic run would be a rough experience.
        var driveLetter = hddLetters[0];

        // Already elevated (the app requests it at launch - see app.manifest) - run the analysis
        // in-process rather than relaunching with "runas", which would show a second consent
        // prompt for something the user already approved once at startup.
        if (ElevationService.IsElevated)
            return MapExitCode(RunElevatedAnalyze(driveLetter), driveLetter);

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return new CheckResult(CheckStatus.Warn, "Couldn't locate the app's own executable to check fragmentation.");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{ElevatedArg} {driveLetter}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED - user clicked "No"
        {
            return new CheckResult(CheckStatus.Warn, $"You declined the administrator prompt, so fragmentation couldn't be checked on drive {driveLetter}.");
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, $"Couldn't check fragmentation: {ex.Message}");
        }

        process.WaitForExit();

        return MapExitCode(process.ExitCode, driveLetter);
    }

    private static CheckResult MapExitCode(int exitCode, string driveLetter) => exitCode switch
    {
        0 => new CheckResult(CheckStatus.Pass, $"Drive {driveLetter} isn't significantly fragmented."),
        1 => new CheckResult(CheckStatus.Warn, $"Drive {driveLetter} is fragmented enough that defragmenting could help performance."),
        _ => new CheckResult(CheckStatus.Warn, $"Couldn't determine fragmentation on drive {driveLetter}."),
    };

    /// <summary>
    /// Runs in the elevated child process only - invoked from App.xaml.cs before any UI is
    /// created. Returns a process exit code (see CheckFragmentation's mapping above).
    /// </summary>
    public static int RunElevatedAnalyze(string driveLetter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Volume WHERE DriveLetter = '{driveLetter}'");
            using var volumes = searcher.Get();

            ManagementObject? volume = null;
            foreach (ManagementObject v in volumes) { volume = v; break; }
            if (volume is null) return 2;

            using (volume)
            {
                using var result = volume.InvokeMethod("DefragAnalysis", null, null);
                if (result is null) return 2;

                var returnValue = Convert.ToUInt32(result["ReturnValue"]);
                if (returnValue != 0) return 2;

                using var data = (ManagementBaseObject)result["DefragAnalysis"];
                var totalFragObj = data["TotalPercentFragmentation"];
                if (totalFragObj is null) return 2;

                var totalFrag = Convert.ToDouble(totalFragObj);
                return totalFrag < 10 ? 0 : 1;
            }
        }
        catch
        {
            return 2;
        }
    }

    /// <summary>Drive letters that sit on a genuine HDD - reuses EnvironmentInfoService's drive
    /// inventory (physical disk + type + letters) rather than reimplementing the same WMI
    /// associator chain a second time.</summary>
    private static List<string> GetHddDriveLetters() =>
        EnvironmentInfoService.GatherDriveInventory()
            .Where(d => d.MediaType == DriveMediaType.Hdd)
            .SelectMany(d => d.DriveLetters)
            .ToList();
}
