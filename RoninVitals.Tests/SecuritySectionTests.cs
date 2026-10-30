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
        Assert.Equal(new[] { "defender", "vitalsfiles", "scanboth" }, Security.Symptoms.Select(symptom => symptom.Id));
    }

    [Fact]
    public void RunBoth_IsExactlyTheTwoOptionsChecksInOrder_SoItCannotDrift()
    {
        var defender = Security.Symptoms.Single(symptom => symptom.Id == "defender").Checks;
        var vitals = Security.Symptoms.Single(symptom => symptom.Id == "vitalsfiles").Checks;
        var both = Security.Symptoms.Single(symptom => symptom.Id == "scanboth").Checks;

        Assert.Equal(defender.Concat(vitals).Select(check => check.Label), both.Select(check => check.Label));
    }

    [Fact]
    public void EveryScanCheckIsRealNeverAScriptedPlaceholder()
    {
        // A scan that shows a canned "all clear" would be worse than no scan at all.
        foreach (var check in Security.Symptoms.SelectMany(symptom => symptom.Checks))
            Assert.True(check.RealCheck is not null, $"'{check.Label}' has no real check behind it.");
    }

    [Fact]
    public void Security_HasItsOwnHeadlineRatherThanTheTroubleshootingQuestion()
    {
        Assert.NotEqual("What seems to be the problem?", Security.Headline);
        Assert.Equal("What seems to be the problem?", VitalsCatalog.GetSection(SectionKey.Network).Headline);
    }
}
