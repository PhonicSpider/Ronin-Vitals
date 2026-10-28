using System.Text;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;
using static RoninVitals.Services.WebAccessRules;

namespace RoninVitals.Tests;

public class WebAccessRulesTests
{
    // ── DNS packets ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildDnsQuery_HasTheStandardHeaderAndEncodedName()
    {
        var packet = BuildDnsQuery("www.microsoft.com", 0xABCD);

        Assert.Equal(0xAB, packet[0]);                    // transaction id, high byte
        Assert.Equal(0xCD, packet[1]);                    // transaction id, low byte
        Assert.Equal(new byte[] { 0x01, 0x00 }, packet[2..4]);   // recursion desired
        Assert.Equal(new byte[] { 0x00, 0x01 }, packet[4..6]);   // exactly one question

        var name = new byte[] { 3 }.Concat(Encoding.ASCII.GetBytes("www"))
            .Concat(new byte[] { 9 }).Concat(Encoding.ASCII.GetBytes("microsoft"))
            .Concat(new byte[] { 3 }).Concat(Encoding.ASCII.GetBytes("com")).Concat(new byte[] { 0 }).ToArray();
        Assert.Equal(name, packet[12..(12 + name.Length)]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x01 }, packet[^4..]); // type A, class IN
    }

    [Fact]
    public void BuildDnsQuery_IgnoresATrailingDot() =>
        Assert.Equal(BuildDnsQuery("example.com", 1), BuildDnsQuery("example.com.", 1));

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData("this-label-is-way-too-long-to-be-a-valid-dns-label-because-the-limit-is-sixty-three-chars.com")]
    public void BuildDnsQuery_RejectsInvalidNames(string name) =>
        Assert.Throws<ArgumentException>(() => BuildDnsQuery(name, 1));

    private static byte[] Reply(ushort id, byte flagsHigh, int rcode, int answers) =>
        new byte[] { (byte)(id >> 8), (byte)id, flagsHigh, (byte)rcode, 0, 1, (byte)(answers >> 8), (byte)answers, 0, 0, 0, 0 };

    [Fact]
    public void ParseDnsReply_RecognizesASuccessfulAnswer()
    {
        var reply = ParseDnsReply(Reply(7, 0x81, rcode: 0, answers: 2), 7);

        Assert.True(reply.Valid);
        Assert.True(reply.Resolved);
        Assert.Equal(2, reply.AnswerCount);
    }

    [Fact]
    public void ParseDnsReply_NameNotFoundIsValidButNotResolved()
    {
        var reply = ParseDnsReply(Reply(7, 0x81, rcode: 3, answers: 0), 7);

        Assert.True(reply.Valid);
        Assert.False(reply.Resolved);
        Assert.Equal(3, reply.ResponseCode);
        Assert.Equal("name not found", DescribeResponseCode(reply.ResponseCode));
    }

    [Fact]
    public void ParseDnsReply_ZeroAnswersIsNotResolvedEvenWithNoError() =>
        Assert.False(ParseDnsReply(Reply(7, 0x81, rcode: 0, answers: 0), 7).Resolved);

    [Fact]
    public void ParseDnsReply_RejectsAReplyWithTheWrongId() =>
        Assert.False(ParseDnsReply(Reply(8, 0x81, 0, 1), 7).Valid);

    [Fact]
    public void ParseDnsReply_RejectsAQueryThatIsNotAResponse() =>
        Assert.False(ParseDnsReply(Reply(7, 0x01, 0, 1), 7).Valid);   // QR bit clear

    [Fact]
    public void ParseDnsReply_RejectsATruncatedPacket() =>
        Assert.False(ParseDnsReply(new byte[] { 0, 7, 0x81 }, 7).Valid);

    // ── Hosts file ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseHostsFile_StockFileWithOnlyCommentsAndLocalhostIsEmpty()
    {
        const string stock = "# Copyright (c) 1993-2009 Microsoft Corp.\r\n#\r\n#      102.54.94.97     rhino.acme.com\r\n\r\n127.0.0.1       localhost\r\n::1             localhost\r\n";
        Assert.Empty(ParseHostsFile(stock));
    }

    [Fact]
    public void ParseHostsFile_FindsBlocksAndRedirectsAndStripsInlineComments()
    {
        const string text = "0.0.0.0 ads.example.com\n127.0.0.1 tracker.example.net # blocked on purpose\n203.0.113.5 login.example.org\n";
        var entries = ParseHostsFile(text);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsBlock);
        Assert.True(entries[1].IsBlock);
        Assert.Equal("tracker.example.net", entries[1].Host);
        Assert.False(entries[2].IsBlock);
    }

    [Fact]
    public void ParseHostsFile_OneLineCanNameSeveralHosts()
    {
        var entries = ParseHostsFile("0.0.0.0 a.example.com b.example.com   c.example.com\n");
        Assert.Equal(new[] { "a.example.com", "b.example.com", "c.example.com" }, entries.Select(e => e.Host));
    }

    [Fact]
    public void ParseHostsFile_IgnoresDockerAndWslInternalNames()
    {
        const string text = "192.168.1.5 host.docker.internal\n192.168.1.5 gateway.docker.internal\n127.0.0.1 kubernetes.docker.internal\n";
        Assert.Empty(ParseHostsFile(text));
    }

    [Fact]
    public void ParseHostsFile_IgnoresSingleWordLocalAliases_ButStillCatchesRealSiteNames()
    {
        // Regression: "127.0.0.1 view-localhost" on a real PC was reported as "blocking a website".
        var entries = ParseHostsFile("127.0.0.1 view-localhost\n127.0.0.1 devbox\n0.0.0.0 steamcommunity.com\n");

        var only = Assert.Single(entries);
        Assert.Equal("steamcommunity.com", only.Host);
    }

    [Fact]
    public void ParseHostsFile_IgnoresLinesThatAreNotAddressMappings()
    {
        Assert.Empty(ParseHostsFile("this is not a mapping\nbanana example.com\n   \n"));
    }

    // ── Clock ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, ClockState.Fine)]
    [InlineData(90, ClockState.Fine)]
    [InlineData(-90, ClockState.Fine)]
    [InlineData(120, ClockState.Fine)]          // the boundary belongs to "fine"
    [InlineData(121, ClockState.Drifting)]
    [InlineData(300, ClockState.Drifting)]
    [InlineData(-600, ClockState.Drifting)]
    [InlineData(601, ClockState.Wrong)]
    [InlineData(-7200, ClockState.Wrong)]
    public void ClassifyClockSkew_UsesTwoAndTenMinuteThresholds_InEitherDirection(int seconds, ClockState expected) =>
        Assert.Equal(expected, ClassifyClockSkew(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(-30, "30 seconds fast")]
    [InlineData(30, "30 seconds slow")]
    [InlineData(-300, "5 minutes fast")]
    [InlineData(7200, "2 hours slow")]
    [InlineData(-172800, "2 days fast")]
    public void DescribeSkew_SaysHowFarAndWhichWay(int seconds, string expected) =>
        Assert.Equal(expected, DescribeSkew(TimeSpan.FromSeconds(seconds)));

    // ── Reading a set of site results ────────────────────────────────────────

    private static SiteResult Site(string name, string group, SiteOutcome outcome) => new(name, group, outcome, 100, outcome.ToString());

    private static List<SiteResult> Everything(SiteOutcome general, SiteOutcome gaming) => new()
    {
        Site("Google", "General", general), Site("Microsoft", "General", general),
        Site("Steam", "Gaming", gaming), Site("Epic Games", "Gaming", gaming),
    };

    [Fact]
    public void SummarizeSites_AllLoadedIsAPass()
    {
        var summary = SummarizeSites(Everything(SiteOutcome.Loaded, SiteOutcome.Loaded));
        Assert.Equal(CheckStatus.Pass, summary.Status);
        Assert.Contains("connection is fine", summary.Text);
    }

    [Fact]
    public void SummarizeSites_NothingLoadingBlamesYourSide_AndSaysWhy()
    {
        var dns = SummarizeSites(Everything(SiteOutcome.DnsFailed, SiteOutcome.DnsFailed));
        var tls = SummarizeSites(Everything(SiteOutcome.TlsFailed, SiteOutcome.TlsFailed));
        var timeout = SummarizeSites(Everything(SiteOutcome.TimedOut, SiteOutcome.TimedOut));

        Assert.All(new[] { dns, tls, timeout }, s => Assert.Equal(CheckStatus.Bad, s.Status));
        Assert.Contains("DNS", dns.Text);
        Assert.Contains("clock", tls.Text);
        Assert.Contains("timed out", timeout.Text);
        Assert.Contains("isn't one site being down", dns.Text);
    }

    [Fact]
    public void SummarizeSites_OnlyGamingFailing_PointsAtThoseServicesNotYourConnection()
    {
        var summary = SummarizeSites(Everything(SiteOutcome.Loaded, SiteOutcome.TimedOut));

        Assert.Equal(CheckStatus.Warn, summary.Status);
        Assert.Contains("Steam, Epic Games", summary.Text);
        Assert.Contains("Everyday sites load fine", summary.Text);
        Assert.Contains("2 of 4", summary.Text);
    }

    [Fact]
    public void SummarizeSites_EverydaySitesFailingToo_SuggestsAPatchyConnection()
    {
        var results = Everything(SiteOutcome.Loaded, SiteOutcome.Loaded);
        results[0] = Site("Google", "General", SiteOutcome.TimedOut);

        var summary = SummarizeSites(results);

        Assert.Equal(CheckStatus.Warn, summary.Status);
        Assert.Contains("patchy connection", summary.Text);
    }

    [Fact]
    public void SummarizeSites_ServerErrorsAreReportedAsTheirTrouble_NotAsUnreachable()
    {
        var summary = SummarizeSites(Everything(SiteOutcome.Loaded, SiteOutcome.ServerError));

        Assert.Equal(CheckStatus.Warn, summary.Status);
        Assert.Contains("answered with a server error", summary.Text);
    }

    [Fact]
    public void SummarizeSites_LongListsAreShortenedWithACount()
    {
        var results = Enumerable.Range(1, 8).Select(i => Site($"Site{i}", "Gaming", SiteOutcome.TimedOut)).ToList();
        results.Add(Site("Google", "General", SiteOutcome.Loaded));

        var summary = SummarizeSites(results);

        Assert.Contains("and 4 more", summary.Text);
    }

    [Fact]
    public void SummarizeSites_NoResultsIsAWarningNotACrash()
    {
        var summary = SummarizeSites(Array.Empty<SiteResult>());
        Assert.Equal(CheckStatus.Warn, summary.Status);
    }

    // ── Captive portal ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(200, "Microsoft Connect Test", false)]
    [InlineData(200, "Microsoft Connect Test\r\n", false)]      // trailing newline is normal
    [InlineData(200, "<html>Please sign in to the hotel Wi-Fi</html>", true)]
    [InlineData(302, "", true)]
    [InlineData(307, "", true)]
    public void LooksLikeCaptivePortal_RecognizesTheSignInPage(int status, string body, bool expected) =>
        Assert.Equal(expected, LooksLikeCaptivePortal(status, body));
}
