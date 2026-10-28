using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>
/// Only the paths that return before anything is written to the action log are covered here: logging goes through
/// Application.Current.Dispatcher, which a headless test host doesn't have (see CHECKLIST notes on test scope).
/// </summary>
public class SafeFixServiceTests
{
    [Fact]
    public async Task ADeclinedFix_ShowsItsOwnMessageUnchanged_AndIsNotReportedAsAnError()
    {
        var outcome = await SafeFixService.ApplyAsync("Clear browser caches", FixTier.SafeAutoFix,
            apply: () => throw new FixDeclinedException("Close Chrome first, then press this again."));

        Assert.False(outcome.Succeeded);
        Assert.Equal("Close Chrome first, then press this again.", outcome.Message);
        Assert.DoesNotContain("went wrong", outcome.Message);
    }

    [Fact]
    public async Task AnUnexpectedException_IsStillReportedAsSomethingGoingWrong()
    {
        var outcome = await SafeFixService.ApplyAsync("x", FixTier.SafeAutoFix,
            apply: () => throw new InvalidOperationException("boom"));

        Assert.False(outcome.Succeeded);
        Assert.Contains("Something went wrong", outcome.Message);
        Assert.Contains("boom", outcome.Message);
    }

    [Fact]
    public async Task AFixThatReturnsFalse_ReportsItDidNotComplete()
    {
        var outcome = await SafeFixService.ApplyAsync("x", FixTier.SafeAutoFix, apply: () => Task.FromResult(false));

        Assert.False(outcome.Succeeded);
        Assert.Equal("The fix didn't complete successfully.", outcome.Message);
    }
}
