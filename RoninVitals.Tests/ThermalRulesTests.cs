using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class ThermalRulesTests
{
    [Theory]
    [InlineData(3010, 27.85)]
    [InlineData(3731.5, 100)]
    public void TenthsOfKelvinToCelsius_ConvertsWindowsReadings(double tenthsOfKelvin, double expectedCelsius) =>
        Assert.Equal(expectedCelsius, ThermalRules.TenthsOfKelvinToCelsius(tenthsOfKelvin), precision: 2);

    [Theory]
    [InlineData(28, CheckStatus.Pass)]
    [InlineData(69.9, CheckStatus.Pass)]
    [InlineData(70, CheckStatus.Warn)]
    [InlineData(84.9, CheckStatus.Warn)]
    [InlineData(85, CheckStatus.Bad)]
    public void JudgeTemperature_UsesSeventyAndEightyFiveDegreeThresholds(double celsius, CheckStatus expected) =>
        Assert.Equal(expected, ThermalRules.JudgeTemperature(celsius).Status);

    [Fact]
    public void JudgeTemperature_SaysItIsNotTheCpuCoreReading() =>
        Assert.Contains("not the CPU core", ThermalRules.JudgeTemperature(30).ResultText);

    [Fact]
    public void JudgeThrottling_LiveThrottlingIsAProblem_RecentEventsAreAWarning_NothingIsAPass()
    {
        Assert.Equal(CheckStatus.Bad, ThermalRules.JudgeThrottling(true, 0).Status);
        Assert.Equal(CheckStatus.Warn, ThermalRules.JudgeThrottling(false, 4).Status);
        Assert.Equal(CheckStatus.Pass, ThermalRules.JudgeThrottling(false, 0).Status);
    }

    [Fact]
    public void JudgeFans_ProblemsWarn_AndTheLimitsAreAlwaysStated()
    {
        Assert.Equal(CheckStatus.Warn, ThermalRules.JudgeFans(3, 1).Status);

        var healthy = ThermalRules.JudgeFans(3, 0);
        Assert.Equal(CheckStatus.Pass, healthy.Status);
        Assert.Contains("can't read fan speed", healthy.ResultText);
        Assert.Contains("doesn't list any fans", ThermalRules.JudgeFans(0, 0).ResultText);
    }

    [Theory]
    [InlineData("A1841308-3541-4FAB-BC81-F71556F20B4A", PowerPlan.PowerSaver)]
    [InlineData("381b4222-f694-41f0-9685-ff5bb260df2e", PowerPlan.Balanced)]
    [InlineData("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", PowerPlan.HighPerformance)]
    [InlineData("e9a42b02-d5df-448d-aa00-03f14749eb61", PowerPlan.Ultimate)]
    [InlineData("00000000-0000-0000-0000-000000000000", PowerPlan.Other)]
    public void IdentifyPowerPlan_RecognizesTheBuiltInPlans(string guid, PowerPlan expected) =>
        Assert.Equal(expected, ThermalRules.IdentifyPowerPlan(guid));

    [Fact]
    public void PowerSaverIsAWarningForSpeedButFineForHeat()
    {
        Assert.Equal(CheckStatus.Warn, ThermalRules.JudgePlanForSpeed(PowerPlan.PowerSaver).Status);
        Assert.Equal(CheckStatus.Pass, ThermalRules.JudgePlanForHeat(PowerPlan.PowerSaver).Status);
    }

    [Fact]
    public void HighPerformanceIsAWarningForHeatButFineForSpeed()
    {
        Assert.Equal(CheckStatus.Warn, ThermalRules.JudgePlanForHeat(PowerPlan.HighPerformance).Status);
        Assert.Equal(CheckStatus.Warn, ThermalRules.JudgePlanForHeat(PowerPlan.Ultimate).Status);
        Assert.Equal(CheckStatus.Pass, ThermalRules.JudgePlanForSpeed(PowerPlan.HighPerformance).Status);
    }
}
