using System.ServiceProcess;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class UpdateRulesTests
{
    [Theory]
    [InlineData("2147942402", "0x80070002")]
    [InlineData("2149842967", "0x80240017")]
    [InlineData("0x80070002", "0x80070002")]
    [InlineData("unknown", "unknown")]
    public void ToHexCode_ShowsTheFormPeopleSearchFor(string raw, string expected) =>
        Assert.Equal(expected, UpdateRules.ToHexCode(raw));

    [Fact]
    public void DescribeErrorCode_KnowsCommonCodes_AndIgnoresCase()
    {
        Assert.Contains("damaged", UpdateRules.DescribeErrorCode("0x80073712"));
        Assert.Equal(UpdateRules.DescribeErrorCode("0x8024402C"), UpdateRules.DescribeErrorCode("0x8024402c"));
        Assert.Null(UpdateRules.DescribeErrorCode("0xDEADBEEF"));
    }

    [Fact]
    public void NoFailures_IsAPass() =>
        Assert.Equal(CheckStatus.Pass, UpdateRules.SummarizeFailures([]).Status);

    [Fact]
    public void AFailureNamesTheUpdateAndExplainsAKnownCode()
    {
        var result = UpdateRules.SummarizeFailures([new UpdateFailure("0x800F0922", "2026-10 Cumulative Update")]);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("2026-10 Cumulative Update", result.ResultText);
        Assert.Contains("isn't enough room", result.ResultText);
    }

    [Fact]
    public void AnUnknownCodeIsStillShown() =>
        Assert.Contains("error 0xDEADBEEF", UpdateRules.SummarizeFailures([new UpdateFailure("0xDEADBEEF", "x")]).ResultText);

    [Fact]
    public void ThreeFailuresIsAProblem()
    {
        var failure = new UpdateFailure("0x80070002", "x");
        Assert.Equal(CheckStatus.Bad, UpdateRules.SummarizeFailures([failure, failure, failure]).Status);
    }

    [Fact]
    public void AStoppedOnDemandServiceIsNormal()
    {
        var result = UpdateRules.JudgeServices([new ServiceState("wuauserv", ServiceControllerStatus.Stopped, ServiceStartMode.Manual)]);

        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public void ADisabledServiceIsNamedAsTheProblem()
    {
        var result = UpdateRules.JudgeServices([
            new ServiceState("bits", ServiceControllerStatus.Running, ServiceStartMode.Automatic),
            new ServiceState("wuauserv", ServiceControllerStatus.Stopped, ServiceStartMode.Disabled)]);

        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.Contains("wuauserv is switched off", result.ResultText);
    }

    [Theory]
    [InlineData(5, CheckStatus.Bad)]
    [InlineData(9.9, CheckStatus.Bad)]
    [InlineData(10, CheckStatus.Warn)]
    [InlineData(19.9, CheckStatus.Warn)]
    [InlineData(20, CheckStatus.Pass)]
    [InlineData(200, CheckStatus.Pass)]
    public void SystemDriveSpace_UsesTenAndTwentyGigabyteThresholds(double freeGigabytes, CheckStatus expected) =>
        Assert.Equal(expected, UpdateRules.JudgeSystemDriveSpace("C:\\", freeGigabytes).Status);

    [Fact]
    public void PendingUpdates_NoneIsAPass_SomeIsAWarningThatNamesThem()
    {
        Assert.Equal(CheckStatus.Pass, UpdateRules.SummarizePendingUpdates([]).Status);

        var result = UpdateRules.SummarizePendingUpdates(["Update A", "Update B", "Update C"]);
        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("3 Windows updates are waiting", result.ResultText);
        Assert.Contains("and 1 more", result.ResultText);
    }

    [Fact]
    public void PendingRestart_NoneIsAPass_SomeIsAWarning()
    {
        Assert.Equal(CheckStatus.Pass, UpdateRules.SummarizePendingRestart([]).Status);
        Assert.Equal(CheckStatus.Warn, UpdateRules.SummarizePendingRestart(["a Windows update"]).Status);
    }

    [Fact]
    public void PendingRestart_RoutineFileReplacementsAloneAreNotAWarning()
    {
        var result = UpdateRules.SummarizePendingRestart([], routineFileReplacements: true);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("routine file replacements", result.ResultText);
    }
}
