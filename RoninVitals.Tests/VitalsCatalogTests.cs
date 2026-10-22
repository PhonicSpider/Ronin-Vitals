using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.Tests;

/// <summary>Data-integrity checks over the static symptom/check/driver catalog - not behavior,
/// just making sure the content itself is well-formed (nothing left half-wired).</summary>
public class VitalsCatalogTests
{
    // ── Sections ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sections_ContainsEveryKeyExactlyOnce()
    {
        var keys = VitalsCatalog.Sections.Select(s => s.Key).ToList();
        var expected = Enum.GetValues<SectionKey>();

        foreach (var key in expected)
            Assert.Single(keys, k => k == key);
    }

    [Fact]
    public void Sections_EveryOneHasAtLeastOneSymptom()
    {
        foreach (var section in VitalsCatalog.Sections)
            Assert.NotEmpty(section.Symptoms);
    }

    [Theory]
    [InlineData(SectionKey.Network)]
    [InlineData(SectionKey.Hardware)]
    [InlineData(SectionKey.Software)]
    [InlineData(SectionKey.System)]
    public void GetSection_ReturnsMatchingKey(SectionKey key)
        => Assert.Equal(key, VitalsCatalog.GetSection(key).Key);

    // ── Symptoms ─────────────────────────────────────────────────────────────

    private static IEnumerable<Symptom> AllSymptoms
        => VitalsCatalog.Sections.SelectMany(s => s.Symptoms);

    [Fact]
    public void Symptoms_EveryOneHasAtLeastOneCheck()
    {
        foreach (var symptom in AllSymptoms)
            Assert.NotEmpty(symptom.Checks);
    }

    [Fact]
    public void Symptoms_IdsAreUniqueAcrossTheWholeCatalog()
    {
        var ids = AllSymptoms.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Symptoms_NoSectionHasMoreThanOneCatchAll()
    {
        // Not every section has a catch-all (Network/Software currently don't) - just guard
        // against two ever being marked in the same section, which would be ambiguous in the UI.
        foreach (var section in VitalsCatalog.Sections)
            Assert.True(section.Symptoms.Count(s => s.IsCatchAll) <= 1, $"{section.Key} has more than one catch-all symptom.");
    }

    // ── Checks ───────────────────────────────────────────────────────────────

    private static IEnumerable<CheckDefinition> AllChecks
        => AllSymptoms.SelectMany(s => s.Checks);

    [Fact]
    public void Checks_EveryApplyFixHasAFixLabel()
    {
        // A fix with no label would have nothing to put on the button.
        foreach (var check in AllChecks.Where(c => c.ApplyFix is not null))
            Assert.False(string.IsNullOrWhiteSpace(check.FixLabel));
    }

    [Fact]
    public void Checks_EveryOneHasNonEmptyLabelAndResultText()
    {
        foreach (var check in AllChecks)
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Label));
            Assert.False(string.IsNullOrWhiteSpace(check.ResultText));
        }
    }

    // ── Drivers ──────────────────────────────────────────────────────────────

    [Fact]
    public void Drivers_CatalogIsNotEmpty()
        => Assert.NotEmpty(VitalsCatalog.Drivers);

    [Fact]
    public void Drivers_EveryEntryHasCoreFieldsPopulated()
    {
        foreach (var driver in VitalsCatalog.Drivers)
        {
            Assert.False(string.IsNullOrWhiteSpace(driver.Vendor));
            Assert.False(string.IsNullOrWhiteSpace(driver.Name));
            Assert.False(string.IsNullOrWhiteSpace(driver.CurrentVersion));
            Assert.False(string.IsNullOrWhiteSpace(driver.LatestVersion));
        }
    }
}
