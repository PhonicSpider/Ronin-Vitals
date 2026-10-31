using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Builds a card's report from the checks that just ran and, only when the user asks, writes it to their Documents folder.
/// Building one changes nothing on the PC and touches no disk. The settable folder exists so tests can use a temporary one,
/// the same pattern as the other tunable statics in this app.
/// </summary>
public static partial class ReportService
{
    public static string ReportsFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ronin Vitals", "Reports");

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    /// <summary>The report as one self-contained HTML page: what the app shows on screen and what a saved file contains.</summary>
    public static string BuildHtml(string title, string subtitle, IReadOnlyList<ReportEntry> entries, DateTimeOffset now) =>
        HtmlReportWriter.Render(new ReportDocument(title, subtitle, now, DescribeThisPc(), entries, ReportFonts.BuildFontFaceCss()));

    /// <summary>Saves under a unique name so earlier reports are kept. Written to a temporary file first so a half-written report never appears.
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it can't be saved.</summary>
    public static string Save(string html, string title, DateTimeOffset now)
    {
        Directory.CreateDirectory(ReportsFolder);

        var baseName = $"RoninVitals-{Slug(title)}-{now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}";
        var finalPath = Path.Combine(ReportsFolder, baseName + ".html");
        for (var copy = 2; File.Exists(finalPath); copy++) finalPath = Path.Combine(ReportsFolder, $"{baseName}-{copy}.html");

        var temporaryPath = finalPath + ".tmp";
        File.WriteAllText(temporaryPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, finalPath);
        return finalPath;
    }

    public static string Slug(string title)
    {
        var slug = NonAlphanumeric().Replace(title.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "report" : slug;
    }

    public static IReadOnlyList<ReportFact> DescribeThisPc() =>
    [
        new ReportFact("Computer", Environment.MachineName),
        new ReportFact("Windows", DescribeWindows(Environment.OSVersion.Version)),
        new ReportFact("Administrator rights", ElevationService.IsElevated ? "Yes, so the checks that need them could run" : "No, so some checks may have been limited"),
        new ReportFact("Ronin Vitals", typeof(ReportService).Assembly.GetName().Version?.ToString(3) ?? "unknown"),
    ];

    /// <summary>Windows 11 still reports itself as version 10.0; the build number (22000 and up) tells them apart.</summary>
    public static string DescribeWindows(Version version) =>
        $"{(version.Major >= 10 && version.Build >= 22000 ? "Windows 11" : "Windows 10")} (build {version.Build})";
}
