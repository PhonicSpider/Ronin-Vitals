using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>Only the parsing is unit-tested. The scan itself drives the real Defender and is verified by running it.</summary>
public class SecurityScanServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void ParseDetections_NothingFoundGivesEmptyList(string json) =>
        Assert.Empty(SecurityScanService.ParseDetections(json));

    [Fact]
    public void ParseDetections_SingleResultComesBackAsAnObjectNotAnArray()
    {
        // PowerShell's ConvertTo-Json emits a bare object when there is exactly one item.
        var found = SecurityScanService.ParseDetections("{\"Name\":\"Virus:DOS/EICAR_Test_File\",\"Resources\":\"file:_C:\\\\Users\\\\x\\\\eicar.com\"}");

        var only = Assert.Single(found);
        Assert.Equal("Virus:DOS/EICAR_Test_File", only.Name);
        Assert.Contains("eicar.com", only.Resources);
    }

    [Fact]
    public void ParseDetections_SeveralResultsComeBackAsAnArray()
    {
        var found = SecurityScanService.ParseDetections("[{\"Name\":\"A\",\"Resources\":\"r1\"},{\"Name\":\"B\",\"Resources\":\"r2\"}]");

        Assert.Equal(2, found.Count);
        Assert.Equal(("A", "r1"), found[0]);
        Assert.Equal(("B", "r2"), found[1]);
    }

    [Fact]
    public void ParseDetections_MissingNameOrLocationGetsAReadablePlaceholder()
    {
        var found = SecurityScanService.ParseDetections("[{\"Name\":null,\"Resources\":null}]");

        var only = Assert.Single(found);
        Assert.Equal("an unnamed threat", only.Name);
        Assert.Equal("an unknown location", only.Resources);
    }
}
