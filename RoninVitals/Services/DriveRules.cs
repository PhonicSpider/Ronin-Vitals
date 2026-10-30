using System.Text.Json;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>What the drive's own reliability counters report. Any value the drive doesn't provide is null.</summary>
public sealed record DiskReliability(string Name, int? WearPercent, int? TemperatureCelsius, long? PowerOnHours, long? UncorrectedReadErrors);

public sealed record DriveSpace(string Name, double FreeGigabytes, double TotalGigabytes);

public static class DriveRules
{
    public const double RoomyGigabytes = 50;
    public const double CriticalGigabytes = 10;
    public const double WarnPercentFree = 15;
    public const double CriticalPercentFree = 5;

    public const int WornPercent = 70;
    public const int NearlyWornOutPercent = 90;
    public const int WarmDriveCelsius = 60;
    public const int HotDriveCelsius = 70;

    /// <summary>Reads the JSON the reliability-counter script prints: one object, or an array of them.</summary>
    public static IReadOnlyList<DiskReliability> ParseReliability(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
        return items.Select(item => new DiskReliability(
            Name: item.TryGetProperty("Name", out var name) ? name.GetString() ?? "Unknown disk" : "Unknown disk",
            WearPercent: ReadInt(item, "Wear"),
            TemperatureCelsius: ReadInt(item, "Temperature"),
            PowerOnHours: ReadLong(item, "PowerOnHours"),
            UncorrectedReadErrors: ReadLong(item, "ReadErrorsUncorrected"))).ToList();
    }

    public static CheckResult JudgeReliability(IReadOnlyList<DiskReliability> disks)
    {
        var withData = disks.Where(disk => disk.WearPercent is not null || disk.TemperatureCelsius is not null || disk.UncorrectedReadErrors is not null).ToList();
        if (withData.Count == 0)
            return new CheckResult(CheckStatus.Pass, "Your drives don't report wear or temperature to Windows, so there's nothing more to read from them.");

        var worn = withData.Where(disk => disk.WearPercent >= WornPercent).ToList();
        var hot = withData.Where(disk => disk.TemperatureCelsius >= WarmDriveCelsius).ToList();
        var erroring = withData.Where(disk => disk.UncorrectedReadErrors > 0).ToList();

        if (worn.Any(disk => disk.WearPercent >= NearlyWornOutPercent))
            return new CheckResult(CheckStatus.Bad, $"{worn.First(disk => disk.WearPercent >= NearlyWornOutPercent).Name} has used up {worn.Max(disk => disk.WearPercent)}% of its life. Back it up and plan to replace it.");

        if (hot.Any(disk => disk.TemperatureCelsius >= HotDriveCelsius))
            return new CheckResult(CheckStatus.Bad, $"{hot.First(disk => disk.TemperatureCelsius >= HotDriveCelsius).Name} is running at {hot.Max(disk => disk.TemperatureCelsius)} °C, which is hot enough to shorten its life.");

        var problems = new List<string>();
        if (worn.Count > 0) problems.Add($"{CheckText.Examples(worn.Select(disk => disk.Name))} has used {worn.Max(disk => disk.WearPercent)}% of its life");
        if (hot.Count > 0) problems.Add($"{CheckText.Examples(hot.Select(disk => disk.Name))} is warm ({hot.Max(disk => disk.TemperatureCelsius)} °C)");
        if (erroring.Count > 0) problems.Add($"{CheckText.Examples(erroring.Select(disk => disk.Name))} has had read errors it couldn't fix");

        return problems.Count > 0
            ? new CheckResult(CheckStatus.Warn, string.Join("; ", problems) + ".")
            : new CheckResult(CheckStatus.Pass, $"Your drives report normal wear, temperature and error counts ({withData.Count} {CheckText.Plural(withData.Count, "drive", "drives")} read).");
    }

    /// <summary>
    /// Judges every drive and reports the worst. A big drive with a small percentage free still has plenty of room, so a drive
    /// only counts as getting full when it is low in percent and below <see cref="RoomyGigabytes"/>.
    /// </summary>
    public static CheckResult JudgeFreeSpace(IReadOnlyList<DriveSpace> drives)
    {
        if (drives.Count == 0)
            return new CheckResult(CheckStatus.Warn, "No fixed drives were found to check.");

        return drives
            .Select(drive => (Drive: drive, Result: JudgeDrive(drive)))
            .OrderByDescending(entry => entry.Result.Status)
            .ThenBy(entry => entry.Drive.FreeGigabytes)
            .First().Result;
    }

    private static CheckResult JudgeDrive(DriveSpace drive)
    {
        var percentFree = 100.0 * drive.FreeGigabytes / drive.TotalGigabytes;
        var room = $"only {drive.FreeGigabytes:0.#} GB free";

        if (drive.FreeGigabytes < CriticalGigabytes || (percentFree <= CriticalPercentFree && drive.FreeGigabytes < RoomyGigabytes))
            return new CheckResult(CheckStatus.Bad, $"{drive.Name} is nearly full - {room}.");
        if (percentFree <= WarnPercentFree && drive.FreeGigabytes < RoomyGigabytes)
            return new CheckResult(CheckStatus.Warn, $"{drive.Name} is getting full - {room}.");
        return new CheckResult(CheckStatus.Pass, "Plenty of free space available.");
    }

    private static int? ReadInt(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static long? ReadLong(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
