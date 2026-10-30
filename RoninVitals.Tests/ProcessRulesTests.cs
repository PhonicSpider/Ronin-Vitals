using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class ProcessRulesTests
{
    private const long Gigabyte = 1_073_741_824;

    [Fact]
    public void GroupByProgram_AddsUpInstancesIgnoringCase_BiggestFirst()
    {
        var groups = ProcessRules.GroupByProgram([("chrome", 100), ("Chrome", 300), ("game", 250)]);

        Assert.Equal("chrome", groups[0].Name);
        Assert.Equal(400, groups[0].WorkingSetBytes);
        Assert.Equal(2, groups[0].InstanceCount);
        Assert.Equal("game", groups[1].Name);
    }

    [Theory]
    [InlineData(1, CheckStatus.Pass)]      // 1 of 16 GB
    [InlineData(4.7, CheckStatus.Pass)]    // just under 30%
    [InlineData(4.9, CheckStatus.Warn)]
    [InlineData(8, CheckStatus.Bad)]       // exactly half
    public void JudgeMemoryUse_UsesShareOfTotalMemory(double gigabytesUsed, CheckStatus expected)
    {
        var ranked = new[] { new ProcessGroup("app", (long)(gigabytesUsed * Gigabyte), 1) };

        Assert.Equal(expected, ProcessRules.JudgeMemoryUse(ranked, 16 * Gigabyte).Status);
    }

    [Fact]
    public void JudgeMemoryUse_SaysHowManyProcessesAProgramIsSpreadOver() =>
        Assert.Contains("across 20 processes", ProcessRules.JudgeMemoryUse([new ProcessGroup("chrome", 6 * Gigabyte, 20)], 16 * Gigabyte).ResultText);

    [Fact]
    public void JudgeMemoryUse_NoDataIsAWarningNotACrash() =>
        Assert.Equal(CheckStatus.Warn, ProcessRules.JudgeMemoryUse([], 16 * Gigabyte).Status);

    [Fact]
    public void JudgeNotResponding_NoneIsAPass_SomeIsAWarningThatNamesThem()
    {
        Assert.Equal(CheckStatus.Pass, ProcessRules.JudgeNotResponding([]).Status);

        var result = ProcessRules.JudgeNotResponding(["game", "editor"]);
        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("game, editor have stopped responding", result.ResultText);
    }
}
