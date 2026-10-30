using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class IntegrityRulesTests
{
    [Theory]
    [InlineData("Windows Resource Protection did not find any integrity violations.", CheckStatus.Pass)]
    [InlineData("Windows Resource Protection found integrity violations.", CheckStatus.Bad)]
    [InlineData("There is a system repair pending which requires reboot to complete.  Restart Windows and run sfc again.", CheckStatus.Warn)]
    [InlineData("Windows Resource Protection could not perform the requested operation.", CheckStatus.Warn)]
    [InlineData("", CheckStatus.Warn)]
    public void InterpretSfcOutput_ReadsTheVerdict(string output, CheckStatus expected) =>
        Assert.Equal(expected, IntegrityRules.InterpretSfcOutput(output).Status);

    [Fact]
    public void InterpretSfcOutput_DidNotFindIsNotMistakenForFound() =>
        Assert.Equal(CheckStatus.Pass, IntegrityRules.InterpretSfcOutput("Windows Resource Protection did not find any integrity violations. found").Status);

    [Theory]
    [InlineData("No component store corruption detected.\r\nThe operation completed successfully.", 0, CheckStatus.Pass)]
    [InlineData("The component store is repairable.", 0, CheckStatus.Bad)]
    [InlineData("The component store is not repairable.", 0, CheckStatus.Bad)]
    [InlineData("Error: 740\r\nElevated permissions are required to run DISM.", 740, CheckStatus.Warn)]
    [InlineData("", 1, CheckStatus.Warn)]
    public void InterpretDismCheckHealth_ReadsTheVerdict(string output, int exitCode, CheckStatus expected) =>
        Assert.Equal(expected, IntegrityRules.InterpretDismCheckHealth(output, exitCode).Status);

    [Theory]
    [InlineData(@"C:\Users\TEMP", true)]
    [InlineData(@"C:\Users\temp", true)]
    [InlineData(@"C:\Users\TEMP.DESKTOP-ABC", true)]
    [InlineData(@"C:\Users\TEMP\", true)]
    [InlineData(@"C:\Users\Temperance", false)]
    [InlineData(@"C:\Users\trawh", false)]
    public void IsTemporaryProfileFolder_RecognizesWindowsTempProfiles(string path, bool expected) =>
        Assert.Equal(expected, IntegrityRules.IsTemporaryProfileFolder(path));

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1001.bak", true)]
    [InlineData("S-1-5-21-1-2-3-1001", false)]
    public void IsBrokenProfileKey_LooksForTheBakSuffix(string keyName, bool expected) =>
        Assert.Equal(expected, IntegrityRules.IsBrokenProfileKey(keyName));

    [Fact]
    public void JudgeProfile_TemporaryIsAProblem_SignsOfTroubleAreAWarning_NothingIsAPass()
    {
        Assert.Equal(CheckStatus.Bad, IntegrityRules.JudgeProfile(true, 0, 0).Status);
        Assert.Equal(CheckStatus.Warn, IntegrityRules.JudgeProfile(false, 1, 0).Status);
        Assert.Equal(CheckStatus.Warn, IntegrityRules.JudgeProfile(false, 0, 2).Status);
        Assert.Equal(CheckStatus.Pass, IntegrityRules.JudgeProfile(false, 0, 0).Status);
    }

    [Fact]
    public void JudgePolicies_OnlyWindowsGroupsCount()
    {
        Assert.Equal(CheckStatus.Pass, IntegrityRules.JudgePolicies(new Dictionary<string, int>()).Status);
        Assert.Equal(CheckStatus.Pass, IntegrityRules.JudgePolicies(new Dictionary<string, int> { ["Edge"] = 14 }).Status);

        var result = IntegrityRules.JudgePolicies(new Dictionary<string, int> { ["Edge"] = 14, ["Windows"] = 20, ["Windows NT"] = 3 });
        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("23 Windows settings are set by policy", result.ResultText);
    }
}
