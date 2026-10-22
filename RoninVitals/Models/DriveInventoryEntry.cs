namespace RoninVitals.Models;

public enum DriveMediaType
{
    Unspecified,
    Hdd,
    Ssd
}

/// <summary>One physical disk found by EnvironmentInfoService, with the drive letters/partitions it hosts.</summary>
public sealed record DriveInventoryEntry(string FriendlyName, DriveMediaType MediaType, IReadOnlyList<string> DriveLetters);
