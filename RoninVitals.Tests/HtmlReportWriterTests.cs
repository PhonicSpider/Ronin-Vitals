using System.Text.RegularExpressions;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class HtmlReportWriterTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 30, 15, 42, 7, TimeSpan.FromHours(-4));

    private static ReportEntry Entry(string label = "Check", CheckStatus status = CheckStatus.Pass, string result = "Fine.", string? guidance = null,
        string? command = null, string? detail = null, params ReportTable[] tables) =>
        new(label, status, result, guidance, command, detail, tables);

    private static string Render(params ReportEntry[] entries) =>
        HtmlReportWriter.Render(new ReportDocument("Security report", "Run every check", Moment, [new ReportFact("Computer", "TEST-PC")], entries));

    private static ReportTable Table(string title = "Ports", bool collapsed = false, params ReportRow[] rows) =>
        new(title, ["Program", "Ports"], rows, Note: "A note.", StartsCollapsed: collapsed);

    // ── Safety: the page must not run anything or trust machine text ────────

    [Fact]
    public void Render_ContainsNoScriptAndNoExternalReferences()
    {
        var html = Render(Entry(), Entry(status: CheckStatus.Warn, tables: Table(rows: new ReportRow(["a", "b"]))));

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(src|href)\s*=\s*""https?:", html);
        Assert.DoesNotMatch(@"@import|url\(\s*['""]?https?:", html);
    }

    [Fact]
    public void Render_ForbidsScriptAndNetworkAccessWithAContentSecurityPolicy()
    {
        var html = Render(Entry());

        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("script-src", html);
    }

    [Fact]
    public void Render_EscapesMarkupInEveryPlaceMachineTextAppears()
    {
        const string hostile = "<script>alert('x')</script>";
        var table = new ReportTable(hostile, [hostile], [new ReportRow([hostile + "\n" + hostile])], Note: hostile);
        var entry = new ReportEntry(hostile, CheckStatus.Warn, hostile, hostile, hostile, hostile, [table]);

        var html = HtmlReportWriter.Render(new ReportDocument(hostile, hostile, Moment, [new ReportFact(hostile, hostile)], [entry]));

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", html);
    }

    [Fact]
    public void Render_CannotBeBrokenOutOfAnAttributeByAQuoteInATableTitle()
    {
        var table = new ReportTable("x\" onfocus=\"alert(1)", ["a"], [new ReportRow(["b"])], StartsCollapsed: true);

        var html = Render(Entry(tables: table));

        Assert.DoesNotContain("onfocus=\"alert", html);
        Assert.Contains("aria-label=\"x&quot; onfocus=&quot;alert(1)\"", html);
    }

    [Fact]
    public void Encode_MakesDirectionOverridesVisibleInsteadOfObeyingThem() =>
        Assert.Equal("invoice&lt;RLO&gt;fdp.exe", HtmlReportWriter.Encode("invoice‮fdp.exe"));

    [Fact]
    public void Encode_ReplacesStrayControlCharactersButKeepsLineBreaksAndTabs() =>
        Assert.Equal("a�b\nc\td", HtmlReportWriter.Encode("a\u0001b\nc\td"));

    [Fact]
    public void Encode_GivesNothingForNullOrEmpty()
    {
        Assert.Equal(string.Empty, HtmlReportWriter.Encode(null));
        Assert.Equal(string.Empty, HtmlReportWriter.Encode(""));
    }

    // ── Structure ───────────────────────────────────────────────────────────

    [Fact]
    public void Render_IsAWellFormedDocumentWithLanguageTitleAndViewport()
    {
        var html = Render(Entry());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<title>Security report - Ronin Vitals</title>", html);
        Assert.Contains("name=\"viewport\"", html);
        Assert.Contains("<h1>Security report</h1>", html);
    }

    [Fact]
    public void Render_ShowsTheMachineFactsAndWhenTheReportWasMade()
    {
        var html = Render(Entry());

        Assert.Contains("<dt>Computer</dt><dd>TEST-PC</dd>", html);
        Assert.Contains("2026-10-30 15:42 UTC-04:00", html);
        Assert.Contains("datetime=\"2026-10-30T15:42:07.0000000-04:00\"", html);
    }

    [Fact]
    public void Render_CountsResultsAndSaysSoInWords()
    {
        var html = Render(Entry(status: CheckStatus.Pass), Entry(status: CheckStatus.Warn), Entry(status: CheckStatus.Warn), Entry(status: CheckStatus.Bad));

        Assert.Contains("<strong>1</strong><span>Pass</span>", html);
        Assert.Contains("<strong>2</strong><span>Worth a look</span>", html);
        Assert.Contains("<strong>1</strong><span>Problem</span>", html);
        Assert.Contains("3 of 4 checks need a look", html);
        Assert.Contains("tone-bad", html);
    }

    [Fact]
    public void Render_SaysEverythingIsClearWhenEveryCheckPasses()
    {
        var html = Render(Entry(), Entry());

        Assert.Contains("Everything checked came back clear.", html);
        Assert.Contains("tone-pass", html);
    }

    [Fact]
    public void Render_WarnsAboutAnEmptyReport() =>
        Assert.Contains("No checks were run.", Render());

    [Fact]
    public void Render_GivesEveryStatusAWordAsWellAsAColour()
    {
        var html = Render(Entry(status: CheckStatus.Pass), Entry(status: CheckStatus.Warn), Entry(status: CheckStatus.Bad));

        Assert.Contains("pill-pass\"><span aria-hidden=\"true\">&#10003;</span> Pass", html);
        Assert.Contains("pill-warn\"><span aria-hidden=\"true\">!</span> Worth a look", html);
        Assert.Contains("pill-bad\"><span aria-hidden=\"true\">&#10005;</span> Problem", html);
    }

    [Fact]
    public void Render_LinksEachGlanceRowToItsSectionWithAMatchingId()
    {
        var html = Render(Entry("First"), Entry("Second"));

        Assert.Contains("<a href=\"#check-1\">First</a>", html);
        Assert.Contains("<a href=\"#check-2\">Second</a>", html);
        Assert.Contains("id=\"check-1\"", html);
        Assert.Contains("id=\"check-2\"", html);
    }

    [Fact]
    public void Render_OffersGuidanceOnlyWhenSomethingNeedsAttention()
    {
        var pass = Render(Entry(status: CheckStatus.Pass, guidance: "Do the thing."));
        var warn = Render(Entry(status: CheckStatus.Warn, guidance: "Do the thing."));

        Assert.DoesNotContain("What to do", pass);
        Assert.Contains("<h3>What to do</h3>", warn);
        Assert.Contains("Do the thing.", warn);
    }

    [Fact]
    public void Render_KeepsTechnicalDetailsFoldedAwayAndOmitsThemWhenThereAreNone()
    {
        var with = Render(Entry(command: "Get-Thing", detail: "raw output"));
        var without = Render(Entry());

        Assert.Contains("<details class=\"tech\"><summary>Technical details</summary>", with);
        Assert.Contains("<pre>Get-Thing</pre>", with);
        Assert.Contains("<pre>raw output</pre>", with);
        Assert.DoesNotContain("Technical details</summary>", without);
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    [Fact]
    public void Render_WritesATableWithColumnHeadersRowsAndLineBreaksInCells()
    {
        var table = Table(rows: new ReportRow(["steam.exe\nService: Steam", "TCP 27036"]));

        var html = Render(Entry(tables: table));

        Assert.Contains("<th scope=\"col\">Program</th>", html);
        Assert.Contains("<td>steam.exe<br>Service: Steam</td>", html);
        Assert.Contains("<td>TCP 27036</td>", html);
        Assert.Contains("Ports <span class=\"count\">(1)</span>", html);
        Assert.Contains("A note.", html);
    }

    [Fact]
    public void Render_MarksNotableAndMutedRowsDifferently()
    {
        var table = Table(rows:
        [
            new ReportRow(["a", "b"], ReportRowEmphasis.Notable),
            new ReportRow(["c", "d"], ReportRowEmphasis.Muted),
            new ReportRow(["e", "f"]),
        ]);

        var html = Render(Entry(tables: table));

        Assert.Contains("<tr class=\"row-notable\">", html);
        Assert.Contains("<tr class=\"row-muted\">", html);
        Assert.Contains("<tr class=\"row-normal\">", html);
    }

    [Fact]
    public void Render_FoldsACollapsedTableIntoADetailsElementAndLeavesOtherTablesOpen()
    {
        var html = Render(Entry(tables: [Table("Open one"), Table("Folded one", collapsed: true)]));

        Assert.Contains("<details class=\"folded\"><summary>Folded one", html);
        Assert.DoesNotContain("<details class=\"folded\"><summary>Open one", html);
        Assert.Contains("<h3 id=\"check-1-table-1-heading\">Open one", html);
    }

    [Fact]
    public void Render_GivesEveryScrollableTableAKeyboardFocusAndAName()
    {
        var html = Render(Entry(tables: Table(rows: new ReportRow(["a", "b"]))));

        var regions = Regex.Matches(html, "<div class=\"table-wrap\" role=\"region\"[^>]*tabindex=\"0\">");
        Assert.Equal(2, regions.Count); // the at-a-glance table and the check's own table
        Assert.All(regions, region => Assert.Contains("aria-label", region.Value));
    }

    // ── Brand ───────────────────────────────────────────────────────────────

    [Fact]
    public void Render_UsesTheAppsOwnColoursFromColorsXaml()
    {
        var html = Render(Entry());

        Assert.Contains("--accent: #e06020", html);
        Assert.Contains("--bg: #161212", html);
        Assert.Contains("--icon: #00d8f0", html);
    }

    [Fact]
    public void Render_InsertsEmbeddedFontRulesWhenGiven()
    {
        var html = HtmlReportWriter.Render(new ReportDocument("T", "", Moment, [], [], FontFaceCss: "@font-face { font-family: \"Space Grotesk\"; }"));

        Assert.Contains("@font-face { font-family: \"Space Grotesk\"; }", html);
    }
}
