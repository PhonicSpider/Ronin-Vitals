using System.Net;
using System.Text;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// The pure logic behind the "a website or game won't load" checks - packet building, text parsing and
/// how to read a set of results - kept free of any network, file or registry access so it can be unit-tested
/// exactly. The checks that touch the machine live in <see cref="WebAccessDiagnosticsService"/>.
/// </summary>
public static class WebAccessRules
{
    // ── DNS over UDP, to ask a public resolver directly and compare with the PC's own ─────────

    public sealed record DnsReply(bool Valid, int ResponseCode, int AnswerCount)
    {
        /// <summary>A usable "yes, here's an address": a real reply, no error code, at least one answer.</summary>
        public bool Resolved => Valid && ResponseCode == 0 && AnswerCount > 0;
    }

    /// <summary>A standard recursive DNS query for an A (IPv4) record, as defined in RFC 1035.</summary>
    public static byte[] BuildDnsQuery(string name, ushort id)
    {
        var packet = new List<byte>
        {
            (byte)(id >> 8), (byte)id,   // transaction id
            0x01, 0x00,                  // flags: recursion desired
            0x00, 0x01,                  // one question
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // no answer/authority/additional records
        };

        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException($"'{name}' is not a valid host name.", nameof(name));
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.Add(0);                   // end of name
        packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // type A, class IN
        return packet.ToArray();
    }

    /// <summary>Reads just the header of a DNS reply: was it ours, what was the result code, how many answers.</summary>
    public static DnsReply ParseDnsReply(byte[] data, ushort expectedId)
    {
        if (data.Length < 12) return new DnsReply(false, -1, 0);
        var id = (ushort)((data[0] << 8) | data[1]);
        var isResponse = (data[2] & 0x80) != 0;
        if (id != expectedId || !isResponse) return new DnsReply(false, -1, 0);

        var responseCode = data[3] & 0x0F;           // 0 = ok, 3 = name does not exist, 2 = server failure, 5 = refused
        var answers = (data[6] << 8) | data[7];
        return new DnsReply(true, responseCode, answers);
    }

    public static string DescribeResponseCode(int code) => code switch
    {
        0 => "ok",
        2 => "server failure",
        3 => "name not found",
        5 => "refused",
        _ => $"error {code}",
    };

    // ── The hosts file ───────────────────────────────────────────────────────────────────────

    public sealed record HostsEntry(string Address, string Host)
    {
        /// <summary>0.0.0.0 / 127.0.0.1 / :: / ::1 as the target means "send this name nowhere": a block.</summary>
        public bool IsBlock => Address is "0.0.0.0" or "127.0.0.1" or "::" or "::1";
    }

    /// <summary>
    /// The active (non-comment) lines of a hosts file, one entry per host name. Ignores the normal built-in
    /// localhost lines, the ".internal" names Docker Desktop and WSL add, and single-word names such as
    /// "view-localhost": a name with no dot is a local alias that nobody could browse to, so it isn't blocking
    /// a website (found on a real machine, where one such alias made the check cry wolf).
    /// </summary>
    public static List<HostsEntry> ParseHostsFile(string text)
    {
        var entries = new List<HostsEntry>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var address = parts[0];
            if (!IPAddress.TryParse(address, out _)) continue; // junk line, not a mapping

            foreach (var host in parts.Skip(1))
            {
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) continue;
                if (host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) continue;
                if (!host.Contains('.')) continue;
                entries.Add(new HostsEntry(address, host));
            }
        }
        return entries;
    }

    // ── The system clock ─────────────────────────────────────────────────────────────────────

    public enum ClockState { Fine, Drifting, Wrong }

    /// <summary>
    /// Secure (HTTPS) sites reject a certificate that looks "not valid yet" or "expired" when your clock is off,
    /// and it takes only minutes of drift for some services to start refusing you. Under two minutes is normal
    /// (the measurement itself is only good to about a second); over ten is wrong enough to break sites.
    /// </summary>
    public static ClockState ClassifyClockSkew(TimeSpan skew)
    {
        var minutes = Math.Abs(skew.TotalMinutes);
        return minutes <= 2 ? ClockState.Fine : minutes <= 10 ? ClockState.Drifting : ClockState.Wrong;
    }

    public static string DescribeSkew(TimeSpan skew)
    {
        var abs = skew.Duration();
        var amount = abs.TotalDays >= 1 ? $"{abs.TotalDays:0.#} days"
            : abs.TotalHours >= 1 ? $"{abs.TotalHours:0.#} hours"
            : abs.TotalMinutes >= 1 ? $"{abs.TotalMinutes:0.#} minutes"
            : $"{abs.TotalSeconds:0} seconds";
        return skew < TimeSpan.Zero ? $"{amount} fast" : $"{amount} slow";
    }

    // ── Reading a set of "does this site load?" results ──────────────────────────────────────

    public enum SiteOutcome { Loaded, ServerError, DnsFailed, TimedOut, TlsFailed, OtherFailure }

    public sealed record SiteResult(string Name, string Group, SiteOutcome Outcome, long Milliseconds, string Detail)
    {
        public bool Failed => Outcome is SiteOutcome.DnsFailed or SiteOutcome.TimedOut or SiteOutcome.TlsFailed or SiteOutcome.OtherFailure;
        public bool Troubled => Failed || Outcome == SiteOutcome.ServerError;
    }

    public sealed record SiteSummary(CheckStatus Status, string Text);

    /// <summary>
    /// Turns the per-site results into one plain-language verdict. The key idea is comparison: if nothing loads, the
    /// problem is on this side (connection, DNS, something blocking everything); if only some sites fail while the rest
    /// load fine, the problem is much more likely on those sites' side, or something on this PC blocking just them.
    /// </summary>
    public static SiteSummary SummarizeSites(IReadOnlyList<SiteResult> results)
    {
        if (results.Count == 0)
            return new SiteSummary(CheckStatus.Warn, "Couldn't test any sites.");

        var failed = results.Where(r => r.Failed).ToList();
        var serverErrors = results.Where(r => r.Outcome == SiteOutcome.ServerError).ToList();
        var troubled = results.Where(r => r.Troubled).ToList();

        if (failed.Count == results.Count)
        {
            var reason = results.All(r => r.Outcome == SiteOutcome.DnsFailed) ? "Every name lookup failed, which points at your DNS."
                : results.All(r => r.Outcome == SiteOutcome.TlsFailed) ? "Every secure connection failed, which points at your clock or at security software inspecting your traffic."
                : results.All(r => r.Outcome == SiteOutcome.TimedOut) ? "Every connection timed out, which points at your connection itself or something blocking all web traffic."
                : "The failures point at your connection rather than at any one site.";
            return new SiteSummary(CheckStatus.Bad, $"None of the {results.Count} sites and game services we tried would load. {reason} This isn't one site being down.");
        }

        if (troubled.Count == 0)
            return new SiteSummary(CheckStatus.Pass, $"All {results.Count} popular sites and game services loaded, so your connection is fine. If one site still won't load, that site is the likely problem.");

        var names = string.Join(", ", troubled.Take(4).Select(r => r.Name)) + (troubled.Count > 4 ? $" and {troubled.Count - 4} more" : "");
        var general = results.Where(r => r.Group == "General").ToList();
        var anyGeneralTroubled = general.Any(r => r.Troubled);

        var verdict = anyGeneralTroubled
            ? "Some everyday sites are failing while others load, which can mean a patchy connection or DNS."
            : "Everyday sites load fine, so your connection works. The ones that failed are most likely having trouble on their side, or something on this PC (a hosts-file entry, a firewall rule, security software) is blocking just them.";
        var kind = serverErrors.Count == troubled.Count ? "answered with a server error" : "couldn't be reached";
        return new SiteSummary(CheckStatus.Warn, $"{troubled.Count} of {results.Count} {kind}: {names}. {verdict}");
    }

    /// <summary>The captive-portal test: Windows' own connectivity page returns this exact text when the network is open.</summary>
    public const string ConnectTestExpectedBody = "Microsoft Connect Test";

    public static bool LooksLikeCaptivePortal(int statusCode, string body) =>
        statusCode is >= 300 and < 400 || (statusCode == 200 && body.Trim() != ConnectTestExpectedBody);
}
