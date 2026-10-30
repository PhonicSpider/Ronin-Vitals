using System.Diagnostics;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class ProcessRunnerTests
{
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public void Run_CapturesOutputAndExitCode()
    {
        var result = ProcessRunner.Run("cmd.exe", "/c echo hello", GenerousTimeout);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public void Run_ReportsANonZeroExitCodeAsNotSucceeded()
    {
        var result = ProcessRunner.Run("cmd.exe", "/c exit 3", GenerousTimeout);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Run_CapturesStandardError()
    {
        var result = ProcessRunner.Run("cmd.exe", "/c echo oops 1>&2", GenerousTimeout);

        Assert.Equal("oops", result.Error);
    }

    [Fact]
    public void Run_KillsAToolThatHangsAndReportsTimedOut()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = ProcessRunner.Run("ping.exe", "-n 30 127.0.0.1", TimeSpan.FromMilliseconds(500));

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "should return soon after the timeout, not when the tool finishes");
    }

    [Fact]
    public void Run_ReportsAMissingProgramInsteadOfThrowing()
    {
        var result = ProcessRunner.Run("this-program-does-not-exist.exe", "", GenerousTimeout);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Run_PassesEachArgumentIntactWhenGivenAList()
    {
        var result = ProcessRunner.Run("cmd.exe", ["/c", "echo", "two words"], GenerousTimeout);

        Assert.Contains("two words", result.Output);
    }
}
