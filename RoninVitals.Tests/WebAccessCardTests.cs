using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class WebAccessCardTests
{
    private static IReadOnlyList<RoninVitals.Models.CheckDefinition> Checks =>
        VitalsCatalog.Sections.SelectMany(s => s.Symptoms).Single(s => s.Id == "cantreach").Checks;

    [Fact]
    public void EveryCheckOnTheCardIsRealNeverAScriptedPlaceholder()
    {
        // The card used to end with "Clearing stale browser cache", which always said "looks fine" without looking.
        foreach (var check in Checks)
            Assert.True(check.RealCheck is not null, $"'{check.Label}' has no real check behind it.");
    }

    [Fact]
    public void EveryButtonOnTheCardDoesSomethingReal()
    {
        // The old firewall check offered "Allow this app through firewall" with nothing wired to it. A fix button must either
        // run a real fix or show guidance text; a button that does neither just flips to "Applied".
        foreach (var check in Checks.Where(c => c.FixLabel is not null))
            Assert.True(check.ApplyFix is not null || !string.IsNullOrWhiteSpace(check.FixInfoText), $"'{check.Label}' has a fix button with nothing behind it.");
    }

    [Fact]
    public void TheIsItMeOrThemCheckComesFirst() =>
        Assert.Contains("popular sites", Checks[0].Label);

    [Fact]
    public void TheTwoRealFixesAreTheSafeAutoTier()
    {
        foreach (var label in new[] { "Checking your clock is right", "Checking your browsers' saved data" })
        {
            var check = Checks.Single(c => c.Label == label);
            Assert.NotNull(check.ApplyFix);
            Assert.Equal(RoninVitals.Models.FixTier.SafeAutoFix, check.Tier);
        }
    }
}
