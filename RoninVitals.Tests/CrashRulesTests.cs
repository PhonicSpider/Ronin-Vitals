using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class CrashRulesTests
{
    private static CrashEvent Crash(string program, string module = "ntdll.dll", bool freeze = false) => new(program, module, freeze);

    [Fact]
    public void NoEvents_IsAPass() =>
        Assert.Equal(CheckStatus.Pass, CrashRules.Summarize([]).Status);

    [Fact]
    public void OneCrash_IsAWarningThatNamesTheProgram()
    {
        var result = CrashRules.Summarize([Crash("game.exe")]);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("game.exe crashed 1 time ", result.ResultText);
    }

    [Fact]
    public void ThreeCrashesOfTheSameProgram_IsAProblem()
    {
        var result = CrashRules.Summarize([Crash("game.exe"), Crash("GAME.EXE"), Crash("game.exe")]);

        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.Contains("3 times", result.ResultText);
    }

    [Fact]
    public void ThreeCrashesAcrossThreePrograms_IsOnlyAWarning() =>
        Assert.Equal(CheckStatus.Warn, CrashRules.Summarize([Crash("a.exe"), Crash("b.exe"), Crash("c.exe")]).Status);

    [Fact]
    public void TheMostCrashedProgramIsNamedAndTheRestAreCounted()
    {
        var result = CrashRules.Summarize([Crash("a.exe"), Crash("b.exe"), Crash("b.exe"), Crash("c.exe")]);

        Assert.StartsWith("b.exe crashed 2 times", result.ResultText);
        Assert.Contains("2 other programs also had problems", result.ResultText);
    }

    [Fact]
    public void FreezesAreWordedAsFreezes()
    {
        Assert.Contains("froze", CrashRules.Summarize([Crash("a.exe", freeze: true)]).ResultText);
        Assert.Contains("crashed or froze", CrashRules.Summarize([Crash("a.exe"), Crash("a.exe", freeze: true)]).ResultText);
    }

    [Theory]
    [InlineData("nvwgf2umx.dll")]
    [InlineData("atidxx64.dll")]
    [InlineData("igd10iumd64.dll")]
    [InlineData("d3d11.dll")]
    public void AGraphicsDriverFileInTheCrashIsCalledOut(string module) =>
        Assert.Contains("graphics driver", CrashRules.Summarize([Crash("game.exe", module)]).ResultText);

    [Fact]
    public void AnOrdinaryModuleDoesNotBlameTheGraphicsDriver() =>
        Assert.DoesNotContain("graphics", CrashRules.Summarize([Crash("game.exe", "KERNELBASE.dll")]).ResultText);

    [Fact]
    public void DescribeEvents_ListsProgramsWithCountsAndModules()
    {
        var detail = CrashRules.DescribeEvents([Crash("a.exe", "x.dll"), Crash("a.exe", "x.dll"), Crash("b.exe", "unknown")]);

        Assert.Equal("a.exe x2 (module x.dll); b.exe x1 (module unknown)", detail);
    }
}
