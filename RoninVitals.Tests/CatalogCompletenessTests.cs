using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>Guards the promise that no card shows invented results or a button that does nothing.</summary>
public class CatalogCompletenessTests
{
    private static IEnumerable<(Symptom Card, CheckDefinition Check)> EveryCheck =>
        VitalsCatalog.Sections.SelectMany(section => section.Symptoms).SelectMany(card => card.Checks.Select(check => (card, check)));

    [Fact]
    public void EveryCheckInTheAppIsRealNeverAScriptedPlaceholder()
    {
        var placeholders = EveryCheck.Where(entry => entry.Check.RealCheck is null).Select(entry => $"{entry.Card.Title}: {entry.Check.Label}").ToList();

        Assert.True(placeholders.Count == 0, "Checks with no real check behind them: " + string.Join("; ", placeholders));
    }

    [Fact]
    public void EveryFixButtonRunsAFixOrShowsGuidance()
    {
        var emptyButtons = EveryCheck
            .Where(entry => entry.Check.FixLabel is not null && entry.Check.ApplyFix is null && string.IsNullOrWhiteSpace(entry.Check.FixInfoText))
            .Select(entry => $"{entry.Card.Title}: {entry.Check.Label}")
            .ToList();

        Assert.True(emptyButtons.Count == 0, "Fix buttons with nothing behind them: " + string.Join("; ", emptyButtons));
    }

    [Fact]
    public void EveryRealFixIsTheSafeTier_BecauseNoneNeedsARestorePoint() =>
        Assert.All(EveryCheck.Where(entry => entry.Check.ApplyFix is not null), entry => Assert.Equal(FixTier.SafeAutoFix, entry.Check.Tier));
}
