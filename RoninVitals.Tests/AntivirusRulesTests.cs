using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class AntivirusRulesTests
{
    private const int OnUpToDate = 0x041000;
    private const int OnOutOfDate = 0x041010;
    private const int Off = 0x040000;
    private const int Snoozed = 0x042000;
    private const int Expired = 0x043000;
    private const int DefenderOn = 0x061100;

    private static AntivirusProduct Product(string name, int productState, string? location = @"C:\Program Files\Some\av.exe") =>
        AntivirusRules.Combine([new AntivirusRegistration(name, productState, location)]).Single();

    // ── Telling Microsoft's own product from the rest ────────────────────────

    [Theory]
    [InlineData("Windows Defender")]
    [InlineData("Microsoft Defender Antivirus")]
    [InlineData("microsoft defender antivirus")]
    public void IsMicrosoftDefender_RecognisesTheNamesWindowsRegistersForItsOwnAntivirus(string name) =>
        Assert.True(AntivirusRules.IsMicrosoftDefender(name));

    [Theory]
    [InlineData("Bitdefender Antivirus Plus")]
    [InlineData("Bitdefender Total Security")]
    [InlineData("ESET Security")]
    [InlineData("Norton 360")]
    [InlineData("McAfee Total Protection")]
    [InlineData("Kaspersky Standard")]
    public void IsMicrosoftDefender_DoesNotMistakeAnotherProductForIt(string name) =>
        Assert.False(AntivirusRules.IsMicrosoftDefender(name));

    // ── Reading Security Center's productState ───────────────────────────────

    [Theory]
    [InlineData(OnUpToDate, AntivirusState.On)]
    [InlineData(OnOutOfDate, AntivirusState.On)]
    [InlineData(DefenderOn, AntivirusState.On)]
    [InlineData(Off, AntivirusState.Off)]
    [InlineData(Snoozed, AntivirusState.Snoozed)]
    [InlineData(Expired, AntivirusState.Expired)]
    public void ReadState_ReadsBitsTwelveToFifteen(int productState, AntivirusState expected) =>
        Assert.Equal(expected, AntivirusRules.ReadState(productState));

    [Theory]
    [InlineData(OnUpToDate, false)]
    [InlineData(OnOutOfDate, true)]
    [InlineData(Off, false)]
    public void AreDefinitionsOutOfDate_ReadsBitsFourToSeven(int productState, bool expected) =>
        Assert.Equal(expected, AntivirusRules.AreDefinitionsOutOfDate(productState));

    // ── One entry per product ────────────────────────────────────────────────

    [Fact]
    public void Combine_ListsAProductOnceEvenWhenWindowsRegistersItSeveralTimes()
    {
        var registrations = new[]
        {
            new AntivirusRegistration("ESET Security", OnUpToDate, @"C:\Program Files\ESET\ecmds.exe"),
            new AntivirusRegistration("ESET Security", OnUpToDate, @"C:\Program Files\ESET\ecmds.exe"),
            new AntivirusRegistration("eset security", OnUpToDate, null),
        };

        var product = Assert.Single(AntivirusRules.Combine(registrations));

        Assert.Equal("ESET Security", product.Name);
        Assert.Equal(@"C:\Program Files\ESET\ecmds.exe", product.Location);
    }

    [Fact]
    public void Combine_KeepsTheMostProtectiveEntryWhenRegistrationsDisagree()
    {
        var registrations = new[]
        {
            new AntivirusRegistration("Example AV", Off, null),
            new AntivirusRegistration("Example AV", OnUpToDate, null),
        };

        Assert.Equal(AntivirusState.On, Assert.Single(AntivirusRules.Combine(registrations)).State);
    }

    [Fact]
    public void Combine_PutsTheProductThatIsOnFirstAndMicrosoftsAfterOtherActiveOnes()
    {
        var products = AntivirusRules.Combine(
        [
            new AntivirusRegistration("Windows Defender", DefenderOn, null),
            new AntivirusRegistration("Old Product", Off, null),
            new AntivirusRegistration("ESET Security", OnUpToDate, null),
        ]);

        Assert.Equal(["ESET Security", "Windows Defender", "Old Product"], products.Select(product => product.Name));
    }

    // ── The verdict ──────────────────────────────────────────────────────────

    [Fact]
    public void Judge_NamesAThirdPartyAntivirusThatIsOnAndCurrent_WhateverItIs()
    {
        var result = AntivirusRules.Judge(AntivirusRules.Combine(
        [
            new AntivirusRegistration("Bitdefender Total Security", OnUpToDate, null),
            new AntivirusRegistration("Windows Defender", Off, null),
        ]));

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("Bitdefender Total Security is your antivirus", result.ResultText);
    }

    [Fact]
    public void Judge_NamesWindowsDefenderWhenItIsTheOnlyOneOn()
    {
        var result = AntivirusRules.Judge([Product("Windows Defender", DefenderOn)]);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("Windows Defender is your antivirus", result.ResultText);
    }

    [Fact]
    public void Judge_DoesNotCountDefenderListedAsOnBesideAnotherAntivirusAsASecondProgram()
    {
        var result = AntivirusRules.Judge(AntivirusRules.Combine(
        [
            new AntivirusRegistration("ESET Security", OnUpToDate, null),
            new AntivirusRegistration("Windows Defender", DefenderOn, null),
        ]));

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.DoesNotContain("Defender", result.ResultText);
    }

    [Fact]
    public void Judge_IsBadWhenWindowsListsNoAntivirus()
    {
        var result = AntivirusRules.Judge([]);

        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.Contains("doesn't list any antivirus", result.ResultText);
    }

    [Theory]
    [InlineData(Off, "ESET Security is switched off")]
    [InlineData(Snoozed, "ESET Security is paused")]
    [InlineData(Expired, "ESET Security has expired")]
    public void Judge_IsBadWhenNothingIsOn_AndSaysWhyForEachProduct(int productState, string expectedText)
    {
        var result = AntivirusRules.Judge([Product("ESET Security", productState)]);

        Assert.Equal(CheckStatus.Bad, result.Status);
        Assert.Contains(expectedText, result.ResultText);
    }

    [Fact]
    public void Judge_WarnsWhenTheAntivirusThatIsOnHasOldDefinitions()
    {
        var result = AntivirusRules.Judge([Product("ESET Security", OnOutOfDate)]);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("ESET Security is switched on, but the virus definitions are out of date", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsWhenTwoThirdPartyAntivirusProgramsAreBothOn()
    {
        var result = AntivirusRules.Judge(AntivirusRules.Combine(
        [
            new AntivirusRegistration("ESET Security", OnUpToDate, null),
            new AntivirusRegistration("Norton 360", OnUpToDate, null),
        ]));

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("ESET Security and Norton 360 are all switched on", result.ResultText);
    }

    // ── The report table ─────────────────────────────────────────────────────

    [Fact]
    public void BuildTables_ListsEveryProductWithItsStatusDefinitionsMakerAndLocation()
    {
        var products = AntivirusRules.Combine(
        [
            new AntivirusRegistration("ESET Security", OnUpToDate, @"C:\Program Files\ESET\ecmds.exe"),
            new AntivirusRegistration("Windows Defender", Off, "windowsdefender://"),
        ]).Select(product => product with { Maker = product.IsMicrosoft ? "Microsoft Corporation" : "ESET, spol. s r.o." }).ToList();

        var table = Assert.Single(AntivirusRules.BuildTables(products));

        Assert.Equal(["Antivirus", "Status", "Virus definitions", "Maker", "Location"], table.Columns);
        Assert.Equal(["ESET Security", "On", "Up to date", "ESET, spol. s r.o.", @"C:\Program Files\ESET\ecmds.exe"], table.Rows[0].Cells);
        Assert.Equal(["Windows Defender", "Off", "Up to date", "Microsoft Corporation", "windowsdefender://"], table.Rows[1].Cells);
    }

    [Fact]
    public void BuildTables_MutesDefenderStandingByAndHighlightsOldDefinitions()
    {
        var table = Assert.Single(AntivirusRules.BuildTables(AntivirusRules.Combine(
        [
            new AntivirusRegistration("ESET Security", OnOutOfDate, null),
            new AntivirusRegistration("Windows Defender", Off, null),
        ])));

        Assert.Equal(ReportRowEmphasis.Notable, table.Rows[0].Emphasis);
        Assert.Equal(ReportRowEmphasis.Muted, table.Rows[1].Emphasis);
    }

    [Fact]
    public void BuildTables_MutesDefenderEvenWhenWindowsListsItAsOnBesideAnotherAntivirus()
    {
        // Windows lists Defender as "on" while it stands by, so its row must not look like a second active antivirus.
        var table = Assert.Single(AntivirusRules.BuildTables(AntivirusRules.Combine(
        [
            new AntivirusRegistration("ESET Security", OnUpToDate, null),
            new AntivirusRegistration("Windows Defender", DefenderOn, null),
        ])));

        Assert.Equal(ReportRowEmphasis.Normal, table.Rows[0].Emphasis);
        Assert.Equal(ReportRowEmphasis.Muted, table.Rows[1].Emphasis);
    }

    [Fact]
    public void BuildTables_HighlightsEveryRowWhenNothingIsOn()
    {
        var table = Assert.Single(AntivirusRules.BuildTables([Product("ESET Security", Off)]));

        Assert.Equal(ReportRowEmphasis.Notable, table.Rows[0].Emphasis);
    }

    [Fact]
    public void BuildTables_HasNothingToShowWhenWindowsListsNoAntivirus() =>
        Assert.Empty(AntivirusRules.BuildTables([]));

    [Fact]
    public void BuildTables_SaysTheListProvesNothingAboutWhetherTheProgramWorks()
    {
        var table = Assert.Single(AntivirusRules.BuildTables([Product("ESET Security", OnUpToDate)]));

        Assert.Contains("doesn't prove it is working", table.Note);
    }
}
