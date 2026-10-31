using System.Globalization;
using System.Net;
using System.Text;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Turns a <see cref="ReportDocument"/> into one self-contained HTML page in the app's colours, like the report Windows makes
/// for the battery. The page carries no script and loads nothing from the internet. Everything in it that came from the machine
/// (program names, task descriptions, file paths) is untrusted text, so every piece is HTML-encoded and invisible
/// direction-changing characters are made visible, instead of being trusted.
/// </summary>
public static class HtmlReportWriter
{
    private static readonly char[] LineBreaks = ['\n'];

    // Bidirectional-control characters can reorder how text is drawn (the "invoice.pdf.exe" disguise); show them instead of obeying them.
    private static readonly Dictionary<char, string> InvisibleCharacters = new()
    {
        ['‪'] = "<LRE>", ['‫'] = "<RLE>", ['‬'] = "<PDF>", ['‭'] = "<LRO>", ['‮'] = "<RLO>",
        ['⁦'] = "<LRI>", ['⁧'] = "<RLI>", ['⁨'] = "<FSI>", ['⁩'] = "<PDI>",
    };

    public static string Render(ReportDocument document)
    {
        var page = new StringBuilder(64 * 1024);
        var counts = CountByStatus(document.Entries);

        page.AppendLine("<!DOCTYPE html>");
        page.AppendLine("<html lang=\"en\">");
        page.AppendLine("<head>");
        page.AppendLine("<meta charset=\"utf-8\">");
        page.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        page.AppendLine("<meta name=\"color-scheme\" content=\"dark\">");
        page.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">");
        page.AppendLine($"<title>{Encode(document.Title)} - Ronin Vitals</title>");
        page.AppendLine("<style>");
        page.AppendLine(document.FontFaceCss);
        page.AppendLine(Stylesheet);
        page.AppendLine("</style>");
        page.AppendLine("</head>");
        page.AppendLine("<body>");
        page.AppendLine("<a class=\"skip\" href=\"#details\">Skip to the details</a>");
        page.AppendLine("<div class=\"page\">");

        AppendMasthead(page, document);
        AppendSummary(page, document.Entries, counts);
        AppendAtAGlance(page, document.Entries);

        page.AppendLine("<main id=\"details\">");
        for (var index = 0; index < document.Entries.Count; index++) AppendEntry(page, document.Entries[index], index + 1);
        page.AppendLine("</main>");

        AppendFooter(page, document.GeneratedAt);
        page.AppendLine("</div>");
        page.AppendLine("</body>");
        page.AppendLine("</html>");
        return page.ToString();
    }

    // ── Pieces of the page ──────────────────────────────────────────────────────────────────

    private static void AppendMasthead(StringBuilder page, ReportDocument document)
    {
        page.AppendLine("<header class=\"masthead\">");
        page.AppendLine("<p class=\"brand\"><svg class=\"mark\" viewBox=\"0 0 32 32\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M2 17h6l3-9 5 17 4-11 2 3h8\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg><span>Ronin Vitals</span></p>");
        page.AppendLine($"<h1>{Encode(document.Title)}</h1>");
        if (!string.IsNullOrWhiteSpace(document.Subtitle)) page.AppendLine($"<p class=\"subtitle\">{Encode(document.Subtitle)}</p>");

        page.AppendLine("<dl class=\"facts\">");
        page.AppendLine($"<div><dt>Report created</dt><dd><time datetime=\"{document.GeneratedAt:O}\">{Encode(FormatMoment(document.GeneratedAt))}</time></dd></div>");
        foreach (var fact in document.Facts)
            page.AppendLine($"<div><dt>{Encode(fact.Label)}</dt><dd>{Encode(fact.Value)}</dd></div>");
        page.AppendLine("</dl>");
        page.AppendLine("</header>");
    }

    private static void AppendSummary(StringBuilder page, IReadOnlyList<ReportEntry> entries, IReadOnlyDictionary<CheckStatus, int> counts)
    {
        var needLook = counts[CheckStatus.Warn] + counts[CheckStatus.Bad];
        var verdict = entries.Count == 0
            ? "No checks were run."
            : needLook == 0
                ? "Everything checked came back clear."
                : $"{needLook} of {entries.Count} {(entries.Count == 1 ? "check needs" : "checks need")} a look. The highlighted items below say what was found and what to do.";
        var tone = counts[CheckStatus.Bad] > 0 ? "bad" : counts[CheckStatus.Warn] > 0 ? "warn" : "pass";

        page.AppendLine($"<section class=\"summary tone-{tone}\" aria-labelledby=\"summary-heading\">");
        page.AppendLine("<h2 id=\"summary-heading\">Summary</h2>");
        page.AppendLine($"<p class=\"verdict\">{Encode(verdict)}</p>");
        page.AppendLine("<ul class=\"tally\">");
        page.AppendLine($"<li class=\"tally-pass\"><strong>{counts[CheckStatus.Pass]}</strong><span>Pass</span></li>");
        page.AppendLine($"<li class=\"tally-warn\"><strong>{counts[CheckStatus.Warn]}</strong><span>Worth a look</span></li>");
        page.AppendLine($"<li class=\"tally-bad\"><strong>{counts[CheckStatus.Bad]}</strong><span>Problem</span></li>");
        page.AppendLine("</ul>");
        page.AppendLine("</section>");
    }

    private static void AppendAtAGlance(StringBuilder page, IReadOnlyList<ReportEntry> entries)
    {
        if (entries.Count == 0) return;

        page.AppendLine("<section aria-labelledby=\"glance-heading\">");
        page.AppendLine("<h2 id=\"glance-heading\">At a glance</h2>");
        page.AppendLine("<div class=\"table-wrap\" role=\"region\" aria-labelledby=\"glance-heading\" tabindex=\"0\">");
        page.AppendLine("<table class=\"glance\"><thead><tr><th scope=\"col\">Result</th><th scope=\"col\">Check</th><th scope=\"col\">What was found</th></tr></thead><tbody>");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            page.AppendLine($"<tr><td>{StatusPill(entry.Status)}</td><td><a href=\"#check-{index + 1}\">{Encode(entry.Label)}</a></td><td>{Encode(entry.ResultText)}</td></tr>");
        }
        page.AppendLine("</tbody></table></div>");
        page.AppendLine("</section>");
    }

    private static void AppendEntry(StringBuilder page, ReportEntry entry, int number)
    {
        page.AppendLine($"<section class=\"check status-{StatusName(entry.Status)}\" id=\"check-{number}\" aria-labelledby=\"check-{number}-heading\">");
        page.AppendLine($"<h2 id=\"check-{number}-heading\"><span class=\"number\">{number}</span>{Encode(entry.Label)} {StatusPill(entry.Status)}</h2>");
        page.AppendLine($"<p class=\"result\">{Encode(entry.ResultText)}</p>");

        if (!string.IsNullOrWhiteSpace(entry.Guidance) && entry.Status is CheckStatus.Warn or CheckStatus.Bad)
        {
            page.AppendLine("<div class=\"advice\"><h3>What to do</h3>");
            page.AppendLine($"<p>{Encode(entry.Guidance)}</p></div>");
        }

        for (var tableIndex = 0; tableIndex < entry.Tables.Count; tableIndex++)
            AppendTable(page, entry.Tables[tableIndex], $"check-{number}-table-{tableIndex + 1}");

        if (!string.IsNullOrWhiteSpace(entry.TechCommand) || !string.IsNullOrWhiteSpace(entry.TechDetail))
        {
            page.AppendLine("<details class=\"tech\"><summary>Technical details</summary>");
            if (!string.IsNullOrWhiteSpace(entry.TechCommand))
                page.AppendLine($"<h3>Command</h3><pre>{Encode(entry.TechCommand)}</pre>");
            if (!string.IsNullOrWhiteSpace(entry.TechDetail))
                page.AppendLine($"<h3>What it returned</h3><pre>{Encode(entry.TechDetail)}</pre>");
            page.AppendLine("</details>");
        }
        page.AppendLine("</section>");
    }

    private static void AppendTable(StringBuilder page, ReportTable table, string id)
    {
        var heading = $"{Encode(table.Title)} <span class=\"count\">({table.Rows.Count})</span>";
        if (table.StartsCollapsed)
        {
            page.AppendLine($"<details class=\"folded\"><summary>{heading}</summary>");
        }
        else
        {
            page.AppendLine("<div class=\"listing\">");
            page.AppendLine($"<h3 id=\"{id}-heading\">{heading}</h3>");
        }

        if (!string.IsNullOrWhiteSpace(table.Note)) page.AppendLine($"<p class=\"note\">{Encode(table.Note)}</p>");

        var labelledBy = table.StartsCollapsed ? string.Empty : $" aria-labelledby=\"{id}-heading\"";
        var ariaLabel = table.StartsCollapsed ? $" aria-label=\"{Encode(table.Title)}\"" : string.Empty;
        page.AppendLine($"<div class=\"table-wrap\" role=\"region\"{labelledBy}{ariaLabel} tabindex=\"0\">");
        page.AppendLine("<table class=\"data\"><thead><tr>");
        foreach (var column in table.Columns) page.AppendLine($"<th scope=\"col\">{Encode(column)}</th>");
        page.AppendLine("</tr></thead><tbody>");
        foreach (var row in table.Rows)
        {
            page.AppendLine($"<tr class=\"{EmphasisClass(row.Emphasis)}\">");
            foreach (var cell in row.Cells) page.AppendLine($"<td>{EncodeLines(cell)}</td>");
            page.AppendLine("</tr>");
        }
        page.AppendLine("</tbody></table></div>");

        page.AppendLine(table.StartsCollapsed ? "</details>" : "</div>");
    }

    private static void AppendFooter(StringBuilder page, DateTimeOffset generatedAt)
    {
        var version = typeof(HtmlReportWriter).Assembly.GetName().Version?.ToString(3) ?? "0";
        page.AppendLine("<footer>");
        page.AppendLine($"<p>Made by Ronin Vitals {Encode(version)} on {Encode(FormatMoment(generatedAt))}. Vitals only looked at this PC to write the report; it didn't change anything.</p>");
        page.AppendLine("<p>This is a snapshot. Programs, ports and scheduled tasks change, so run the checks again for a fresh one. " +
                        "The report includes your computer name and the locations of programs on it, so look it over before you share it.</p>");
        page.AppendLine("</footer>");
    }

    // ── Small helpers ───────────────────────────────────────────────────────────────────────

    private static Dictionary<CheckStatus, int> CountByStatus(IReadOnlyList<ReportEntry> entries) =>
        new()
        {
            [CheckStatus.Pass] = entries.Count(entry => entry.Status == CheckStatus.Pass),
            [CheckStatus.Warn] = entries.Count(entry => entry.Status == CheckStatus.Warn),
            [CheckStatus.Bad] = entries.Count(entry => entry.Status == CheckStatus.Bad),
        };

    private static string StatusName(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "pass",
        CheckStatus.Warn => "warn",
        CheckStatus.Bad => "bad",
        _ => "unknown",
    };

    /// <summary>A coloured label that also says the result in words, so colour is never the only signal.</summary>
    private static string StatusPill(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "<span class=\"pill pill-pass\"><span aria-hidden=\"true\">&#10003;</span> Pass</span>",
        CheckStatus.Warn => "<span class=\"pill pill-warn\"><span aria-hidden=\"true\">!</span> Worth a look</span>",
        CheckStatus.Bad => "<span class=\"pill pill-bad\"><span aria-hidden=\"true\">&#10005;</span> Problem</span>",
        _ => "<span class=\"pill pill-unknown\">Not run</span>",
    };

    private static string EmphasisClass(ReportRowEmphasis emphasis) => emphasis switch
    {
        ReportRowEmphasis.Notable => "row-notable",
        ReportRowEmphasis.Muted => "row-muted",
        _ => "row-normal",
    };

    private static string FormatMoment(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd HH:mm 'UTC'zzz", CultureInfo.InvariantCulture);

    /// <summary>Encodes text for HTML and replaces direction-changing characters with visible markers.</summary>
    public static string Encode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var visible = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (InvisibleCharacters.TryGetValue(character, out var marker)) visible.Append(marker);
            else if (char.IsControl(character) && character is not ('\n' or '\r' or '\t')) visible.Append('�');
            else visible.Append(character);
        }
        return WebUtility.HtmlEncode(visible.ToString());
    }

    /// <summary>Encodes each line separately and joins them with line breaks, for table cells that hold several lines.</summary>
    private static string EncodeLines(string text) =>
        string.Join("<br>", text.Replace("\r\n", "\n").Split(LineBreaks).Select(Encode));

    // ── Look and feel: the app's own colours (Resources/Colors.xaml) ────────────────────────

    private const string Stylesheet = """
        :root {
          --bg: #161212; --bg-glow: #420202;
          --surface: #0e0e0e; --surface-2: #171111; --surface-3: #201818;
          --border: #241c1c; --border-strong: #3a2a2a;
          --accent: #e06020; --accent-deep: #b04010; --icon: #00d8f0;
          --text: #e6e3e3; --muted: #c2c0c2; --dim: #9a9898;
          --pass: #04b004; --warn: #c8862e; --bad: #cc2200;
          --on-accent: #2a1200;
          --font-display: "Space Grotesk", "Segoe UI Variable Display", "Segoe UI", system-ui, sans-serif;
          --font-body: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
          --font-mono: "IBM Plex Mono", Consolas, "Cascadia Mono", monospace;
          color-scheme: dark;
        }
        * { box-sizing: border-box; }
        html { -webkit-text-size-adjust: 100%; }
        body {
          margin: 0; color: var(--text); font: 15px/1.55 var(--font-body);
          background: var(--bg) radial-gradient(ellipse 70% 55% at 0% 0%, var(--bg-glow) 0%, transparent 70%) no-repeat;
          min-height: 100vh;
        }
        .skip { position: absolute; left: -999px; top: 0; background: var(--accent); color: var(--on-accent); padding: .5rem 1rem; z-index: 10; }
        .skip:focus { left: 1rem; top: 1rem; }
        .page { max-width: 1180px; margin: 0 auto; padding: clamp(16px, 4vw, 40px) 16px 56px; }
        a { color: var(--icon); text-underline-offset: 3px; }
        a:focus-visible, .table-wrap:focus-visible, summary:focus-visible { outline: 2px solid var(--icon); outline-offset: 3px; border-radius: 4px; }
        h1, h2, h3 { font-family: var(--font-display); font-weight: 600; line-height: 1.2; margin: 0; }
        h2 { font-size: 1.35rem; display: flex; flex-wrap: wrap; align-items: center; gap: .6rem; }
        h3 { font-size: 1rem; color: var(--muted); margin: 1.2rem 0 .5rem; }
        section, .masthead { margin-bottom: 1.75rem; }

        .masthead { padding-bottom: 1.25rem; border-bottom: 1px solid var(--border-strong); }
        .brand { display: flex; align-items: center; gap: .6rem; margin: 0 0 1rem; color: var(--accent); font: 600 .95rem var(--font-display); letter-spacing: .18em; text-transform: uppercase; }
        .mark { width: 30px; height: 30px; }
        h1 { font-size: clamp(1.9rem, 5vw, 2.8rem); }
        .subtitle { margin: .35rem 0 0; color: var(--muted); font-size: 1.05rem; }
        .facts { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: .75rem 1.5rem; margin: 1.4rem 0 0; }
        .facts div { background: var(--surface-2); border: 1px solid var(--border); border-radius: 10px; padding: .6rem .85rem; }
        .facts dt { color: var(--dim); font-size: .75rem; letter-spacing: .08em; text-transform: uppercase; }
        .facts dd { margin: .1rem 0 0; overflow-wrap: anywhere; }

        .summary { background: var(--surface); border: 1px solid var(--border); border-left: 4px solid var(--pass); border-radius: 12px; padding: 1.1rem 1.25rem; }
        .summary.tone-warn { border-left-color: var(--warn); } .summary.tone-bad { border-left-color: var(--bad); }
        .verdict { margin: .5rem 0 1rem; font-size: 1.05rem; }
        .tally { display: flex; flex-wrap: wrap; gap: .75rem; list-style: none; margin: 0; padding: 0; }
        .tally li { background: var(--surface-3); border: 1px solid var(--border); border-radius: 10px; padding: .55rem 1rem; min-width: 7.5rem; display: flex; align-items: baseline; gap: .6rem; }
        .tally strong { font: 600 1.7rem var(--font-display); }
        .tally span { color: var(--muted); font-size: .85rem; }
        .tally-pass strong { color: var(--pass); } .tally-warn strong { color: var(--warn); } .tally-bad strong { color: #ff6a4d; }

        .pill { display: inline-flex; align-items: center; gap: .35rem; border-radius: 999px; padding: .12rem .7rem; font: 600 .78rem var(--font-body); white-space: nowrap; letter-spacing: .02em; }
        .pill-pass { background: var(--pass); color: #031203; }
        .pill-warn { background: var(--warn); color: var(--on-accent); }
        .pill-bad { background: var(--bad); color: #fff; }
        .pill-unknown { background: var(--surface-3); color: var(--muted); border: 1px solid var(--border-strong); }

        .check { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 1.1rem 1.25rem; }
        .check.status-warn { border-left: 4px solid var(--warn); } .check.status-bad { border-left: 4px solid var(--bad); } .check.status-pass { border-left: 4px solid var(--pass); }
        .number { display: inline-grid; place-items: center; width: 1.9rem; height: 1.9rem; border-radius: 50%; background: var(--surface-3); border: 1px solid var(--border-strong); color: var(--icon); font-size: .9rem; }
        .result { margin: .7rem 0 0; }
        .advice { margin-top: 1rem; background: var(--surface-2); border: 1px solid var(--border-strong); border-radius: 10px; padding: .2rem 1rem .9rem; }
        .advice h3 { color: var(--accent); }
        .advice p { margin: 0; color: var(--muted); }
        .note { color: var(--muted); font-size: .9rem; margin: .2rem 0 .7rem; max-width: 80ch; }
        .count { color: var(--dim); font-weight: 400; }

        .table-wrap { overflow-x: auto; border: 1px solid var(--border); border-radius: 10px; background: var(--surface-2); }
        table { border-collapse: collapse; width: 100%; font-size: .88rem; }
        th, td { text-align: left; vertical-align: top; padding: .55rem .75rem; border-bottom: 1px solid var(--border); overflow-wrap: break-word; word-break: normal; }
        th { position: sticky; top: 0; background: var(--surface-3); color: var(--muted); font: 600 .75rem var(--font-body); letter-spacing: .07em; text-transform: uppercase; white-space: nowrap; }
        tbody tr:last-child td { border-bottom: 0; }
        table.data td { min-width: 8rem; overflow-wrap: anywhere; }
        table.glance td:first-child { white-space: nowrap; width: 1%; }
        table.glance td:nth-child(2) { min-width: 11rem; }
        table.glance td:nth-child(3) { min-width: 16rem; }
        .row-notable td { background: rgba(224, 96, 32, .10); }
        .row-notable td:first-child { box-shadow: inset 3px 0 0 var(--accent); }
        .row-muted td { color: var(--dim); }

        details.folded, details.tech { margin-top: 1rem; }
        summary { cursor: pointer; font: 600 1rem var(--font-display); color: var(--muted); padding: .4rem 0; }
        summary:hover { color: var(--text); }
        details.tech { border-top: 1px solid var(--border); padding-top: .5rem; }
        pre { margin: 0 0 .5rem; padding: .8rem 1rem; background: var(--surface-2); border: 1px solid var(--border); border-radius: 8px; color: var(--muted); font: .82rem/1.5 var(--font-mono); white-space: pre-wrap; overflow-wrap: anywhere; }

        footer { margin-top: 2rem; padding-top: 1rem; border-top: 1px solid var(--border-strong); color: var(--dim); font-size: .85rem; max-width: 80ch; }
        footer p { margin: .3rem 0; }

        @media (prefers-reduced-motion: no-preference) { html { scroll-behavior: smooth; } }
        @media print {
          :root { --bg: #fff; --surface: #fff; --surface-2: #fff; --surface-3: #f1f1f1; --border: #c9c9c9; --border-strong: #999; --text: #111; --muted: #333; --dim: #555; --icon: #0a5a66; --accent: #a8420f; color-scheme: light; }
          body { background: #fff; font-size: 11pt; }
          .skip { display: none; }
          .row-notable td { background: #fbe9dd; }
          .check, .summary { break-inside: avoid; }
          .pill { border: 1px solid #333; }
          .pill-pass, .pill-warn, .pill-bad { background: #fff; color: #111; }
        }
        """;
}
