using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class BootRulesTests
{
    [Fact]
    public void JudgeBootTimes_NoDataIsAPassThatSaysNothingWasMeasured()
    {
        var result = BootRules.JudgeBootTimes([]);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("isn't recording", result.ResultText);
    }

    [Theory]
    [InlineData(25, CheckStatus.Pass)]
    [InlineData(59, CheckStatus.Pass)]
    [InlineData(60, CheckStatus.Warn)]
    [InlineData(119, CheckStatus.Warn)]
    [InlineData(120, CheckStatus.Bad)]
    public void JudgeBootTimes_UsesTheAverageAgainstSixtyAndOneTwentySeconds(double seconds, CheckStatus expected) =>
        Assert.Equal(expected, BootRules.JudgeBootTimes([seconds, seconds]).Status);

    [Fact]
    public void JudgeBootTimes_AveragesSeveralBoots() =>
        Assert.Contains("40 seconds", BootRules.JudgeBootTimes([20, 60]).ResultText);

    [Fact]
    public void JudgeShutdowns_CleanIsAPass() =>
        Assert.Equal(CheckStatus.Pass, BootRules.JudgeShutdowns(0, []).Status);

    [Theory]
    [InlineData(1, CheckStatus.Warn)]
    [InlineData(2, CheckStatus.Warn)]
    [InlineData(3, CheckStatus.Bad)]
    public void JudgeShutdowns_RepeatedUnexpectedShutdownsAreAProblem(int count, CheckStatus expected) =>
        Assert.Equal(expected, BootRules.JudgeShutdowns(count, []).Status);

    [Fact]
    public void JudgeShutdowns_ABlueScreenIsAlwaysAProblemAndShowsTheStopCode()
    {
        var result = BootRules.JudgeShutdowns(1, ["0x0000009f"]);

        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.Contains("0x0000009f", result.ResultText);
    }

    [Theory]
    [InlineData("0x0000009F (0xffffa80, 0x2, 0x3, 0x4)", "0x0000009f")]
    [InlineData("The bugcheck was: 0x00000133.", "0x00000133")]
    [InlineData("no code here", null)]
    public void ExtractStopCode_FindsTheHexCode(string text, string? expected) =>
        Assert.Equal(expected, BootRules.ExtractStopCode(text));

    [Theory]
    [InlineData("<WinreLocation path=\"\\Recovery\\WindowsRE\" />\r\n  <InstallState state=\"1\"/>\r\n  <OsInstallAvailable state=\"0\"/>", CheckStatus.Pass)]
    [InlineData("<InstallState state=\"0\"/>", CheckStatus.Warn)]
    [InlineData("<OsInstallAvailable state=\"1\"/>", CheckStatus.Warn)]
    [InlineData("", CheckStatus.Warn)]
    public void JudgeRecoveryEnvironment_ReadsInstallState(string xml, CheckStatus expected) =>
        Assert.Equal(expected, BootRules.JudgeRecoveryEnvironment(xml).Status);

    [Theory]
    [InlineData("Volume - C: is NOT Dirty", DirtyState.Clean)]
    [InlineData("Volume - C: is Dirty", DirtyState.Dirty)]
    [InlineData("Error:  Access is denied.", DirtyState.Unknown)]
    [InlineData("", DirtyState.Unknown)]
    public void InterpretDirtyQuery_DistinguishesDirtyFromNotDirty(string output, DirtyState expected) =>
        Assert.Equal(expected, BootRules.InterpretDirtyQuery(output));

    [Fact]
    public void JudgeDirtyDrives_ADirtyDriveIsNamed_AllUnknownIsAWarning_CleanIsAPass()
    {
        var dirty = BootRules.JudgeDirtyDrives([("C:", DirtyState.Clean), ("D:", DirtyState.Dirty)]);
        Assert.Equal(CheckStatus.Bad, dirty.Status);
        Assert.Contains("D:", dirty.ResultText);

        Assert.Equal(CheckStatus.Warn, BootRules.JudgeDirtyDrives([("C:", DirtyState.Unknown)]).Status);
        Assert.Equal(CheckStatus.Warn, BootRules.JudgeDirtyDrives([]).Status);
        Assert.Equal(CheckStatus.Pass, BootRules.JudgeDirtyDrives([("C:", DirtyState.Clean), ("D:", DirtyState.Unknown)]).Status);
    }
}
