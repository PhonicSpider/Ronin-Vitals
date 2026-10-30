using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class ErrorRulesTests
{
    private static List<LoggedError> Repeat(string source, int id, int count) =>
        Enumerable.Repeat(new LoggedError(source, id), count).ToList();

    [Fact]
    public void NoErrors_IsAPass() =>
        Assert.Equal(CheckStatus.Pass, ErrorRules.SummarizeSystemErrors([]).Status);

    [Fact]
    public void RoutineDistributedComNoiseAloneIsAPass_AndSaysItWasSetAside()
    {
        var result = ErrorRules.SummarizeSystemErrors(Repeat("Microsoft-Windows-DistributedCOM", 10016, 40));

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("40 routine messages were set aside", result.ResultText);
    }

    [Fact]
    public void RealErrorsAreCountedAndTheTopSourceIsNamed()
    {
        var errors = Repeat("Service Control Manager", 7000, 7).Concat(Repeat("ctxapdriver", 52, 2)).Concat(Repeat("Microsoft-Windows-DistributedCOM", 10010, 30)).ToList();

        var result = ErrorRules.SummarizeSystemErrors(errors);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("9 system errors", result.ResultText);
        Assert.Contains("Service Control Manager (event 7000: a service failed to start)", result.ResultText);
    }

    [Theory]
    [InlineData(10, CheckStatus.Warn)]
    [InlineData(11, CheckStatus.Bad)]
    public void MoreThanTenRealErrorsIsAProblem(int count, CheckStatus expected) =>
        Assert.Equal(expected, ErrorRules.SummarizeSystemErrors(Repeat("SomeDriver", 1, count)).Status);

    [Fact]
    public void DescribeSources_ListsTheBiggestFirstAndMarksRoutineOnes()
    {
        var detail = ErrorRules.DescribeSources(Repeat("A", 1, 2).Concat(Repeat("Microsoft-Windows-DistributedCOM", 10016, 5)).ToList());

        Assert.Equal("Microsoft-Windows-DistributedCOM 10016 x5 (routine); A 1 x2", detail);
    }
}
