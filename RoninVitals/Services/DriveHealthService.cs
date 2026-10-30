using System.Management;

namespace RoninVitals.Services;

public enum DriveHealthLevel
{
    Healthy,
    Warning,
    Unhealthy,
    Unknown
}

/// <param name="DiskSummaries">One "name: status" entry per physical disk, for Tech Mode.</param>
public sealed record DriveHealthResult(DriveHealthLevel Level, int TotalDisks, IReadOnlyList<string> ConcerningDisks, IReadOnlyList<string>? DiskSummaries = null);

/// <summary>
/// Read-only physical-disk health via MSFT_PhysicalDisk.HealthStatus in the
/// root\Microsoft\Windows\Storage namespace - the same WMI class PowerShell's
/// Get-PhysicalDisk reads from. This is strictly a query: no repair, no partition or
/// volume changes, nothing that writes to the disk. It cannot brick a drive and cannot
/// trip BitLocker, which reacts to boot/TPM/partition-table changes, not to WMI reads.
/// Do not add write/repair calls here - see the "safe fix whitelist" item on the build
/// checklist before wiring up anything that touches the disk itself.
/// </summary>
public static class DriveHealthService
{
    public static DriveHealthResult GetOverallHealth()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk");

            var concerning = new List<string>();
            var summaries = new List<string>();
            var total = 0;
            var worst = DriveHealthLevel.Healthy;

            foreach (ManagementBaseObject disk in searcher.Get())
            {
                using (disk)
                {
                    total++;
                    var name = disk["FriendlyName"]?.ToString() ?? "Unknown disk";
                    var level = ToLevel(disk["HealthStatus"]);
                    summaries.Add($"{name}: {level}");

                    if (level == DriveHealthLevel.Unhealthy)
                    {
                        worst = DriveHealthLevel.Unhealthy;
                        concerning.Add($"{name} (unhealthy)");
                    }
                    else if (level == DriveHealthLevel.Warning)
                    {
                        if (worst != DriveHealthLevel.Unhealthy) worst = DriveHealthLevel.Warning;
                        concerning.Add($"{name} (warning)");
                    }
                }
            }

            return new DriveHealthResult(total == 0 ? DriveHealthLevel.Unknown : worst, total, concerning, summaries);
        }
        catch (ManagementException)
        {
            // Storage WMI provider unavailable on this system - report unknown rather than guessing.
            return new DriveHealthResult(DriveHealthLevel.Unknown, 0, Array.Empty<string>());
        }
        catch (UnauthorizedAccessException)
        {
            return new DriveHealthResult(DriveHealthLevel.Unknown, 0, Array.Empty<string>());
        }
    }

    private static DriveHealthLevel ToLevel(object? healthStatus)
    {
        if (healthStatus is null) return DriveHealthLevel.Unknown;
        return Convert.ToUInt16(healthStatus) switch
        {
            0 => DriveHealthLevel.Healthy,
            1 => DriveHealthLevel.Warning,
            2 => DriveHealthLevel.Unhealthy,
            _ => DriveHealthLevel.Unknown,
        };
    }
}
