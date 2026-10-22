using System.Management;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Builds a fresh snapshot of attached drives (physical disk + type + drive letters/partitions).
/// The first real category wired into the "Gathering information..." pre-flight step shown when
/// a symptom card expands (SymptomCardViewModel.GatherInformationAsync) - re-verifies what the
/// vitals strip shows and maps each physical disk to its drive letters. Read-only WMI, same APIs
/// DriveHealthService already uses. Also the single source of truth other checks
/// (DriveFragmentationService) use for disk-type/letter mapping, instead of each reimplementing
/// the same WMI associator chain.
///
/// Later categories (network connection type, installed apps, OS/driver versions) follow this
/// same pattern as they're added - see CHECKLIST.md.
/// </summary>
public static class EnvironmentInfoService
{
    public static Task<IReadOnlyList<DriveInventoryEntry>> GatherDriveInventoryAsync() => Task.Run(GatherDriveInventory);

    public static IReadOnlyList<DriveInventoryEntry> GatherDriveInventory()
    {
        var entries = new List<DriveInventoryEntry>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, MediaType, DeviceId FROM MSFT_PhysicalDisk");
            foreach (ManagementBaseObject disk in searcher.Get())
            {
                using (disk)
                {
                    var name = disk["FriendlyName"] as string ?? "Unknown disk";
                    var mediaType = Convert.ToUInt16(disk["MediaType"]) switch
                    {
                        3 => DriveMediaType.Hdd,
                        4 => DriveMediaType.Ssd,
                        _ => DriveMediaType.Unspecified,
                    };
                    var letters = disk["DeviceId"] is string idText && uint.TryParse(idText, out var index)
                        ? GetDriveLettersForIndex(index)
                        : Array.Empty<string>();
                    entries.Add(new DriveInventoryEntry(name, mediaType, letters));
                }
            }
        }
        catch (ManagementException)
        {
            // Degrade gracefully - callers just see an empty inventory rather than a crash.
        }
        return entries;
    }

    private static IReadOnlyList<string> GetDriveLettersForIndex(uint index)
    {
        var letters = new List<string>();
        try
        {
            using var driveSearcher = new ManagementObjectSearcher($"SELECT DeviceID FROM Win32_DiskDrive WHERE Index = {index}");
            foreach (ManagementObject drive in driveSearcher.Get())
            {
                using (drive)
                {
                    using var partitions = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{drive["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition").Get();
                    foreach (ManagementObject partition in partitions)
                    {
                        using (partition)
                        {
                            using var logicalDisks = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition").Get();
                            foreach (ManagementObject logicalDisk in logicalDisks)
                            {
                                using (logicalDisk)
                                {
                                    if (logicalDisk["DeviceID"] is string letter) letters.Add(letter);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // Degrade gracefully - this disk just reports no known letters.
        }
        return letters;
    }
}
