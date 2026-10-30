using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>Runs a real PowerShell process, so each test is bounded: a hang fails instead of stalling the run.</summary>
public class PowerShellConsoleServiceTests
{
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(30);

    private static async Task<string> RunAsync(params string[] commands)
    {
        using var shell = await PowerShellConsoleService.CreateAsync();
        var output = string.Empty;
        foreach (var command in commands)
            output = await shell.RunCommandAsync(command).WaitAsync(HangLimit);
        return output;
    }

    [Fact]
    public async Task ReturnsOnlyTheCommandsOutput() =>
        Assert.Equal("hello", await RunAsync("Write-Output hello"));

    [Fact]
    public async Task KeepsVariablesBetweenCommands() =>
        Assert.Equal("42", await RunAsync("$answer = 42", "$answer"));

    [Fact]
    public async Task KeepsMultiLineOutputInOrder() =>
        Assert.Equal("1\r\n2\r\n3", await RunAsync("1..3"));

    [Fact]
    public async Task ShowsErrorsInsteadOfDroppingThem()
    {
        var output = await RunAsync(@"Get-Item C:\definitely\missing\folder");

        Assert.Contains(@"C:\definitely\missing\folder", output);
    }

    [Fact]
    public async Task ATypoThatCannotParseReturnsAnErrorInsteadOfHanging()
    {
        var output = await RunAsync("Get-Date '");

        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    [Fact]
    public async Task ThousandsOfErrorsDoNotFillThePipeAndFreezeTheShell()
    {
        var output = await RunAsync("1..3000 | ForEach-Object { Write-Error \"failure number $_\" }");

        Assert.Contains("failure number 3000", output);
    }

    [Fact]
    public async Task StillWorksAfterACommandFailed() =>
        Assert.Equal("ok", await RunAsync("Get-Date '", "Write-Output ok"));
}
