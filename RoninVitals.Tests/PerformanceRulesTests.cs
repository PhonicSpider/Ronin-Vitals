using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class PerformanceRulesTests
{
    [Theory]
    [InlineData(0, CheckStatus.Pass)]
    [InlineData(6, CheckStatus.Pass)]
    [InlineData(7, CheckStatus.Warn)]
    [InlineData(20, CheckStatus.Warn)]
    [InlineData(21, CheckStatus.Bad)]
    [InlineData(90, CheckStatus.Bad)]
    public void JudgeTimeSinceRestart_UsesSevenAndTwentyOneDayThresholds(int days, CheckStatus expected) =>
        Assert.Equal(expected, PerformanceRules.JudgeTimeSinceRestart(TimeSpan.FromDays(days)).Status);

    [Fact]
    public void JudgeTimeSinceRestart_SaysHowLong() =>
        Assert.Contains("12 days", PerformanceRules.JudgeTimeSinceRestart(TimeSpan.FromDays(12.5)).ResultText);
}
