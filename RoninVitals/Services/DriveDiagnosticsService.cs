using System.IO;
using System.Text.Json;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only drive checks for the drive cards: Windows' health verdict and the drives' own wear, temperature and error counters.</summary>
public static class DriveDiagnosticsService
{
    private static readonly TimeSpan ReliabilityTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Windows' own good / warning / failing verdict for each physical disk (see DriveHealthService).</summary>
    public static CheckResult CheckDriveHealth()
    {
        const string techCommand = "Get-PhysicalDisk | Select FriendlyName,MediaType,HealthStatus,OperationalStatus";
        var health = DriveHealthService.GetOverallHealth();
        var partitionCount = DriveInfo.GetDrives().Count(drive => drive.DriveType == DriveType.Fixed && drive.IsReady);
        var detail = health.DiskSummaries is { Count: > 0 } ? string.Join("; ", health.DiskSummaries) : "No physical disks reported.";

        var result = health.Level switch
        {
            DriveHealthLevel.Healthy => new CheckResult(CheckStatus.Pass, HealthyDriveMessage(health.TotalDisks, partitionCount)),
            DriveHealthLevel.Warning => new CheckResult(CheckStatus.Warn, $"{string.Join(", ", health.ConcerningDisks)} showing early warning signs."),
            DriveHealthLevel.Unhealthy => new CheckResult(CheckStatus.Bad, $"{string.Join(", ", health.ConcerningDisks)} reporting a failure warning - back up now."),
            _ => new CheckResult(CheckStatus.Warn, "Couldn't read drive health status on this system."),
        };
        return result with { TechCommand = techCommand, TechDetail = detail };
    }

    /// <summary>Phrasing scales with disk count so it never contradicts the vitals strip's drive-letter count (physical disks and partitions differ).</summary>
    private static string HealthyDriveMessage(int diskCount, int partitionCount) => diskCount switch
    {
        1 => "Your hard drive has come back healthy across all partitions.",
        2 => $"Both hard drives have come back healthy across {partitionCount} drive partitions.",
        _ => $"All {diskCount} hard drives report healthy across {partitionCount} drive partitions.",
    };

    /// <summary>Reads SSD wear, temperature and uncorrected read errors. Windows only allows this with administrator rights.</summary>
    public static CheckResult CheckDriveWear()
    {
        const string techCommand = "Get-PhysicalDisk | Get-StorageReliabilityCounter | Select DeviceId,Wear,Temperature,PowerOnHours,ReadErrorsUncorrected";
        const string script =
            "$disks = @(Get-PhysicalDisk | ForEach-Object { $counter = $_ | Get-StorageReliabilityCounter; " +
            "[pscustomobject]@{ Name = $_.FriendlyName; Wear = $counter.Wear; Temperature = $counter.Temperature; PowerOnHours = $counter.PowerOnHours; ReadErrorsUncorrected = $counter.ReadErrorsUncorrected } }); " +
            "ConvertTo-Json -InputObject $disks -Compress";

        if (!ElevationService.IsElevated)
            return new CheckResult(CheckStatus.Warn, "Reading drive wear and temperature needs administrator rights.", techCommand, "The app isn't running as administrator.");

        var run = PowerShellRunner.Run(script, ReliabilityTimeout);
        if (!run.Succeeded)
            return new CheckResult(CheckStatus.Warn, "Couldn't read wear and temperature from your drives.", techCommand, run.TimedOut ? "Timed out." : run.Error);

        try
        {
            var disks = DriveRules.ParseReliability(run.Output);
            var detail = string.Join("; ", disks.Select(disk => $"{disk.Name}: wear {Show(disk.WearPercent, "%")}, {Show(disk.TemperatureCelsius, " °C")}, {Show(disk.PowerOnHours, " h")} powered on, {Show(disk.UncorrectedReadErrors, "")} read errors"));
            return DriveRules.JudgeReliability(disks) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (JsonException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't understand what the drives reported.", techCommand, CheckText.DescribeException(exception));
        }
    }

    private static string Show<T>(T? value, string unit) where T : struct => value is null ? "not reported" : $"{value}{unit}";
}
