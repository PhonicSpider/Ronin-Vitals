using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class DriveRulesTests
{
    private static DiskReliability Disk(string name = "Disk", int? wear = null, int? temperature = null, long? errors = null) =>
        new(name, wear, temperature, PowerOnHours: 1000, errors);

    [Fact]
    public void ParseReliability_ReadsAnArrayAndTreatsNullsAsNotReported()
    {
        var json = """[{"Name":"Samsung SSD","Wear":4,"Temperature":35,"PowerOnHours":1200,"ReadErrorsUncorrected":0},{"Name":"Seagate HDD","Wear":null,"Temperature":null,"PowerOnHours":null,"ReadErrorsUncorrected":null}]""";

        var disks = DriveRules.ParseReliability(json);

        Assert.Equal(2, disks.Count);
        Assert.Equal(4, disks[0].WearPercent);
        Assert.Equal(1200, disks[0].PowerOnHours);
        Assert.Null(disks[1].WearPercent);
        Assert.Null(disks[1].TemperatureCelsius);
    }

    [Fact]
    public void ParseReliability_AcceptsASingleObjectAndEmptyOutput()
    {
        Assert.Single(DriveRules.ParseReliability("""{"Name":"Only disk","Wear":1}"""));
        Assert.Empty(DriveRules.ParseReliability(""));
        Assert.Empty(DriveRules.ParseReliability("[]"));
    }

    [Fact]
    public void NothingReported_IsAPassThatSaysSo() =>
        Assert.Contains("don't report", DriveRules.JudgeReliability([Disk(), Disk()]).ResultText);

    [Theory]
    [InlineData(5, CheckStatus.Pass)]
    [InlineData(69, CheckStatus.Pass)]
    [InlineData(70, CheckStatus.Warn)]
    [InlineData(89, CheckStatus.Warn)]
    [InlineData(90, CheckStatus.Bad)]
    public void Wear_UsesSeventyAndNinetyPercentThresholds(int wear, CheckStatus expected) =>
        Assert.Equal(expected, DriveRules.JudgeReliability([Disk(wear: wear)]).Status);

    [Theory]
    [InlineData(40, CheckStatus.Pass)]
    [InlineData(60, CheckStatus.Warn)]
    [InlineData(70, CheckStatus.Bad)]
    public void Temperature_UsesSixtyAndSeventyDegreeThresholds(int celsius, CheckStatus expected) =>
        Assert.Equal(expected, DriveRules.JudgeReliability([Disk(temperature: celsius)]).Status);

    [Fact]
    public void UncorrectedReadErrorsAreAWarning() =>
        Assert.Equal(CheckStatus.Warn, DriveRules.JudgeReliability([Disk(wear: 2, errors: 3)]).Status);

    [Fact]
    public void TheWornDriveIsNamed() =>
        Assert.Contains("Old SSD", DriveRules.JudgeReliability([Disk("New SSD", wear: 1), Disk("Old SSD", wear: 95)]).ResultText);
}

public class DriveFreeSpaceTests
{
    private static DriveSpace Drive(string name, double freeGigabytes, double totalGigabytes) => new(name, freeGigabytes, totalGigabytes);

    [Fact]
    public void ABigDriveWithAFewPercentFreeStillHasPlentyOfRoom() =>
        Assert.Equal(CheckStatus.Pass, DriveRules.JudgeFreeSpace([Drive("H:\\", 162.6, 1863)]).Status);

    [Theory]
    [InlineData(9, 500, CheckStatus.Bad)]      // under 10 GB is always critical
    [InlineData(20, 500, CheckStatus.Bad)]     // 4% and under 50 GB
    [InlineData(40, 500, CheckStatus.Warn)]    // 8% and under 50 GB
    [InlineData(60, 500, CheckStatus.Pass)]    // 12% but 60 GB is roomy
    [InlineData(100, 500, CheckStatus.Pass)]
    public void SmallAndMidSizeDrivesUseBothPercentAndGigabytes(double free, double total, CheckStatus expected) =>
        Assert.Equal(expected, DriveRules.JudgeFreeSpace([Drive("C:\\", free, total)]).Status);

    [Fact]
    public void TheWorstDriveIsReported_NotJustTheOneWithTheLowestPercent()
    {
        var result = DriveRules.JudgeFreeSpace([Drive("D:\\", 150, 1000), Drive("E:\\", 12, 100)]);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("E:\\", result.ResultText);
    }

    [Fact]
    public void NoDrivesIsAWarning() =>
        Assert.Equal(CheckStatus.Warn, DriveRules.JudgeFreeSpace([]).Status);
}
