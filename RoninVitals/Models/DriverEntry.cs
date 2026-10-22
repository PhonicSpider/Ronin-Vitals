namespace RoninVitals.Models;

public enum DriverUpdateMethod
{
    /// <summary>Microsoft-vetted Windows Update optional driver channel - detect, list, install directly.</summary>
    WindowsUpdate,
    /// <summary>OEM/vendor catalog feed, installed from the vendor's own CDN at install time.</summary>
    Direct,
    /// <summary>GPU, chipset, and BIOS - link-out only, never auto-installed.</summary>
    Manual
}

public sealed class DriverEntry
{
    public required string Vendor { get; init; }
    public required string Name { get; init; }
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required DriverUpdateMethod Method { get; init; }
}
