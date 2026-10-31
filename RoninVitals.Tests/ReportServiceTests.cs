using System.IO;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>Uses a temporary reports folder; ReportService's settable folder is shared, so these tests run one at a time.</summary>
[Collection(ReportServiceCollection.Name)]
public sealed class ReportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 30, 15, 42, 7, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RoninVitalsReportTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _originalFolder = ReportService.ReportsFolder;

    public ReportServiceTests() => ReportService.ReportsFolder = _folder;

    public void Dispose()
    {
        ReportService.ReportsFolder = _originalFolder;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static IReadOnlyList<ReportEntry> OneEntry() => [new ReportEntry("Check", CheckStatus.Pass, "Fine.", null, null, null, [])];

    [Fact]
    public void Save_CreatesTheFolderAndWritesTheHtmlWithADatedName()
    {
        var path = ReportService.Save("<html>hello</html>", "Security report", Moment);

        Assert.Equal(Path.Combine(_folder, "RoninVitals-security-report-2026-10-30-154207.html"), path);
        Assert.Equal("<html>hello</html>", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WritesUtf8WithoutAByteOrderMark()
    {
        var path = ReportService.Save("é", "Security report", Moment);

        Assert.Equal([0xC3, 0xA9], File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_KeepsEarlierReportsByNumberingASecondOneMadeInTheSameSecond()
    {
        var first = ReportService.Save("one", "Security report", Moment);
        var second = ReportService.Save("two", "Security report", Moment);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2.html", second);
        Assert.Equal("one", File.ReadAllText(first));
        Assert.Equal("two", File.ReadAllText(second));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        ReportService.Save("x", "Security report", Moment);

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Theory]
    [InlineData("Security report", "security-report")]
    [InlineData("  Network & Wi-Fi!  ", "network-wi-fi")]
    [InlineData("../../evil", "evil")]
    [InlineData("???", "report")]
    public void Slug_KeepsOnlyLettersAndDigits_SoATitleCanNeverEscapeTheFolder(string title, string expected) =>
        Assert.Equal(expected, ReportService.Slug(title));

    [Fact]
    public void BuildHtml_RendersTheReportWithoutWritingAnything()
    {
        var html = ReportService.BuildHtml("Security report", "Run every check", OneEntry(), Moment);

        Assert.Contains("<h1>Security report</h1>", html);
        Assert.Contains("Run every check", html);
        Assert.Contains("<dt>Computer</dt>", html);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void BuildHtml_EmbedsTheAppsFontsSoTheReportLooksRightOnAnyPc()
    {
        var html = ReportService.BuildHtml("Security report", "x", OneEntry(), Moment);

        Assert.Contains("font-family: \"Space Grotesk\"", html);
        Assert.Contains("font-family: \"IBM Plex Mono\"", html);
        Assert.Contains("data:font/ttf;base64,", html);
    }

    [Theory]
    [InlineData(10, 0, 26200, "Windows 11 (build 26200)")]
    [InlineData(10, 0, 22000, "Windows 11 (build 22000)")]
    [InlineData(10, 0, 19045, "Windows 10 (build 19045)")]
    public void DescribeWindows_TellsWindows11From10ByBuildNumber(int major, int minor, int build, string expected) =>
        Assert.Equal(expected, ReportService.DescribeWindows(new Version(major, minor, build)));

    [Fact]
    public void DescribeThisPc_NamesTheComputerAndTheAppVersion()
    {
        var facts = ReportService.DescribeThisPc().ToDictionary(fact => fact.Label, fact => fact.Value);

        Assert.Equal(Environment.MachineName, facts["Computer"]);
        Assert.Contains("Windows", facts["Windows"]);
        Assert.False(string.IsNullOrWhiteSpace(facts["Ronin Vitals"]));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public static class ReportServiceCollection
{
    public const string Name = "ReportService statics";
}
