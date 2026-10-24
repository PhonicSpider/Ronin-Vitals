using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class SecuritySectionTests
{
    private static Section Security => VitalsCatalog.GetSection(SectionKey.Security);

    [Fact]
    public void Security_OffersDefenderVitalsAndBoth()
    {
        Assert.Equal(new[] { "defender", "vitalsfiles", "scanboth" }, Security.Symptoms.Select(s => s.Id));
    }

    [Fact]
    public void RunBoth_IsExactlyTheTwoOptionsChecksInOrder_SoItCannotDrift()
    {
        var defender = Security.Symptoms.Single(s => s.Id == "defender").Checks;
        var vitals = Security.Symptoms.Single(s => s.Id == "vitalsfiles").Checks;
        var both = Security.Symptoms.Single(s => s.Id == "scanboth").Checks;

        Assert.Equal(defender.Concat(vitals).Select(c => c.Label), both.Select(c => c.Label));
    }

    [Fact]
    public void EveryScanCheckIsRealNeverAScriptedPlaceholder()
    {
        // A scan that shows a canned "all clear" would be worse than no scan at all.
        foreach (var check in Security.Symptoms.SelectMany(s => s.Checks))
            Assert.True(check.RealCheck is not null, $"'{check.Label}' has no real check behind it.");
    }

    [Fact]
    public void Security_HasItsOwnHeadlineRatherThanTheTroubleshootingQuestion()
    {
        Assert.NotEqual("What seems to be the problem?", Security.Headline);
        Assert.Equal("What seems to be the problem?", VitalsCatalog.GetSection(SectionKey.Network).Headline);
    }
}
