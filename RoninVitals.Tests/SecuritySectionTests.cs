using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class SecuritySectionTests
{
    private static Section Security => VitalsCatalog.GetSection(SectionKey.Security);

    [Fact]
    public void Security_OffersDefenderFilesPortsAndTasksAndRunAll()
    {
        Assert.Equal(new[] { "defender", "vitalsfiles", "portstasks", "scanall" }, Security.Symptoms.Select(symptom => symptom.Id));
    }

    [Fact]
    public void RunAll_IsExactlyTheOtherOptionsChecksInOrder_SoItCannotDrift()
    {
        var singleOptions = Security.Symptoms.Where(symptom => symptom.Id != "scanall").SelectMany(symptom => symptom.Checks);
        var all = Security.Symptoms.Single(symptom => symptom.Id == "scanall").Checks;

        Assert.Equal(singleOptions.Select(check => check.Label), all.Select(check => check.Label));
    }

    [Fact]
    public void AntivirusOption_StartsByNamingTheAntivirusBeforeAnyDefenderCheck()
    {
        var checks = Security.Symptoms.Single(symptom => symptom.Id == "defender").Checks;

        Assert.Equal(4, checks.Count);
        Assert.Contains("which antivirus", checks[0].Label);
        Assert.Equal(AntivirusDiagnosticsService.CheckAntivirus, checks[0].RealCheck);
    }

    [Fact]
    public void PortsAndTasksOption_HasTheTwoNewChecks()
    {
        var checks = Security.Symptoms.Single(symptom => symptom.Id == "portstasks").Checks;

        Assert.Equal(2, checks.Count);
        Assert.Contains(checks, check => check.Label.Contains("ports"));
        Assert.Contains(checks, check => check.Label.Contains("scheduled tasks"));
    }

    [Fact]
    public void EverySecurityOptionOffersTheSecurityReport() =>
        Assert.All(Security.Symptoms, symptom => Assert.Equal("Security report", symptom.ReportTitle));

    [Fact]
    public void OtherAreasDoNotOfferAReportYet() =>
        Assert.All(
            VitalsCatalog.Sections.Where(section => section.Key != SectionKey.Security).SelectMany(section => section.Symptoms),
            symptom => Assert.Null(symptom.ReportTitle));

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
