using System.Text.Json;
using RoninVitals.Models;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class PortRulesTests
{
    private const string WindowsDirectory = @"C:\Windows";

    private static PortOwner Owner(int processId, string name, string? path, string? description = null, string? company = null, params HostedService[] services) =>
        new(processId, name, path, description, company, HasValidSignature: null, services);

    private static PortOwner WindowsService(int processId = 1200) =>
        Owner(processId, "svchost.exe", @"C:\Windows\System32\svchost.exe", "Host Process for Windows Services", "Microsoft Corporation",
            new HostedService("RpcSs", "Remote Procedure Call (RPC)", "Serves as the endpoint mapper."));

    private static OpenPort Port(string protocol, int number, PortOwner owner, params string[] addresses) =>
        new(protocol, number, addresses.Length == 0 ? ["0.0.0.0"] : addresses, owner);

    // ── Reading Windows' answer ─────────────────────────────────────────────

    [Fact]
    public void ParseListeners_ReadsAnArrayOfSockets()
    {
        const string json = """[{"Protocol":"TCP","LocalAddress":"0.0.0.0","LocalPort":135,"OwningProcess":1200},{"Protocol":"UDP","LocalAddress":"::","LocalPort":5353,"OwningProcess":1844}]""";

        var listeners = PortRules.ParseListeners(json);

        Assert.Equal([new PortListener("TCP", "0.0.0.0", 135, 1200), new PortListener("UDP", "::", 5353, 1844)], listeners);
    }

    [Fact]
    public void ParseListeners_ReadsASingleObjectBecausePowerShellDoesNotWrapOneResult()
    {
        const string json = """{"Protocol":"TCP","LocalAddress":"127.0.0.1","LocalPort":8080,"OwningProcess":99}""";

        Assert.Equal([new PortListener("TCP", "127.0.0.1", 8080, 99)], PortRules.ParseListeners(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseListeners_GivesNothingForEmptyOutput(string json) => Assert.Empty(PortRules.ParseListeners(json));

    [Fact]
    public void ParseListeners_SkipsEntriesMissingAPortOrOwner()
    {
        const string json = """[{"Protocol":"TCP","LocalAddress":"0.0.0.0","OwningProcess":5},{"Protocol":"TCP","LocalAddress":"0.0.0.0","LocalPort":80},{"Protocol":"TCP","LocalAddress":"0.0.0.0","LocalPort":443,"OwningProcess":6}]""";

        var listeners = PortRules.ParseListeners(json);

        Assert.Equal(443, Assert.Single(listeners).Port);
    }

    [Fact]
    public void ParseListeners_ThrowsOnOutputThatIsNotJson() =>
        Assert.ThrowsAny<JsonException>(() => PortRules.ParseListeners("not json at all"));

    // ── Loopback ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.5.5.5", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::", false)]
    [InlineData("192.168.1.20", false)]
    [InlineData("fe80::1%5", false)]
    [InlineData("garbage", false)]
    public void IsLoopback_RecognisesAddressesThatNeverLeaveThisPc(string address, bool expected) =>
        Assert.Equal(expected, PortRules.IsLoopback(address));

    [Fact]
    public void IsReachableFromNetwork_FalseWhenEveryAddressIsLoopback() =>
        Assert.False(Port("TCP", 8080, WindowsService(), "127.0.0.1", "::1").IsReachableFromNetwork);

    [Fact]
    public void IsReachableFromNetwork_TrueWhenAnyAddressLeavesThePc() =>
        Assert.True(Port("TCP", 80, WindowsService(), "127.0.0.1", "192.168.1.20").IsReachableFromNetwork);

    // ── What counts as a Windows default ────────────────────────────────────

    [Fact]
    public void IsWindowsDefault_TrueForTheSystemProcess() =>
        Assert.True(PortRules.IsWindowsDefault(Owner(PortRules.SystemProcessId, "System", null), WindowsDirectory));

    [Fact]
    public void IsWindowsDefault_TrueForProgramsInWindowsOwnFolders()
    {
        Assert.True(PortRules.IsWindowsDefault(WindowsService(), WindowsDirectory));
        Assert.True(PortRules.IsWindowsDefault(Owner(7, "spoolsv.exe", @"c:\windows\system32\spoolsv.exe"), WindowsDirectory));
        Assert.True(PortRules.IsWindowsDefault(Owner(8, "x.exe", @"C:\Windows\SysWOW64\x.exe"), WindowsDirectory));
    }

    [Fact]
    public void IsWindowsDefault_FalseForASvchostThatIsNotInWindowsFolder_BecauseNamesCanBeFaked() =>
        Assert.False(PortRules.IsWindowsDefault(Owner(9, "svchost.exe", @"C:\Users\Sam\AppData\Local\Temp\svchost.exe"), WindowsDirectory));

    [Fact]
    public void IsWindowsDefault_FalseForAFolderThatOnlyStartsWithTheWindowsPath() =>
        Assert.False(PortRules.IsWindowsDefault(Owner(10, "a.exe", @"C:\Windows\System32Evil\a.exe"), WindowsDirectory));

    [Fact]
    public void IsWindowsDefault_FalseForWindowsTempAndOtherWindowsSubfolders() =>
        Assert.False(PortRules.IsWindowsDefault(Owner(11, "a.exe", @"C:\Windows\Temp\a.exe"), WindowsDirectory));

    [Fact]
    public void IsWindowsDefault_FalseWhenTheProgramCannotBeLocated() =>
        Assert.False(PortRules.IsWindowsDefault(Owner(12, "mystery.exe", null), WindowsDirectory));

    [Fact]
    public void IsWindowsDefault_ToleratesATrailingSlashOnTheWindowsDirectory() =>
        Assert.True(PortRules.IsWindowsDefault(WindowsService(), @"C:\Windows\"));

    // ── Grouping ────────────────────────────────────────────────────────────

    [Fact]
    public void Group_MergesIpv4AndIpv6ForTheSameProgramAndPort()
    {
        var listeners = new[]
        {
            new PortListener("TCP", "0.0.0.0", 80, 5),
            new PortListener("TCP", "::", 80, 5),
            new PortListener("TCP", "0.0.0.0", 80, 5),
        };

        var ports = PortRules.Group(listeners, pid => Owner(pid, "web.exe", @"C:\Tools\web.exe"));

        var port = Assert.Single(ports);
        Assert.Equal(["0.0.0.0", "::"], port.Addresses);
    }

    [Fact]
    public void Group_KeepsTcpAndUdpOnTheSameNumberApart_AndSortsByPort()
    {
        var listeners = new[]
        {
            new PortListener("UDP", "0.0.0.0", 5353, 1),
            new PortListener("TCP", "0.0.0.0", 445, 4),
            new PortListener("TCP", "0.0.0.0", 5353, 1),
        };

        var ports = PortRules.Group(listeners, PortRules.UnknownOwner);

        Assert.Equal([(445, "TCP"), (5353, "TCP"), (5353, "UDP")], ports.Select(port => (port.Port, port.Protocol)));
    }

    // ── Judgment ────────────────────────────────────────────────────────────

    [Fact]
    public void Judge_PassesWhenOnlyWindowsIsListening()
    {
        var ports = new[] { Port("TCP", 135, WindowsService()), Port("TCP", 445, Owner(4, "System", null)) };

        var result = PortRules.Judge(ports, WindowsDirectory);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("Only Windows itself", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsWhenAnotherProgramAcceptsConnectionsFromTheNetwork()
    {
        var ports = new[]
        {
            Port("TCP", 135, WindowsService()),
            Port("TCP", 27036, Owner(20, "steam.exe", @"C:\Program Files (x86)\Steam\steam.exe"), "0.0.0.0"),
        };

        var result = PortRules.Judge(ports, WindowsDirectory);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("steam.exe", result.ResultText);
        Assert.Contains("reached from other devices", result.ResultText);
    }

    [Fact]
    public void Judge_PassesWhenEveryNetworkReachableProgramHasAValidSignature()
    {
        var signedSteam = Owner(20, "steam.exe", @"C:\Program Files (x86)\Steam\steam.exe") with { HasValidSignature = true };
        var ports = new[] { Port("TCP", 135, WindowsService()), Port("TCP", 27036, signedSteam, "0.0.0.0") };

        var result = PortRules.Judge(ports, WindowsDirectory);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("valid digital signature", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsWhenASignedProgramRunsFromADownloadsFolderAndIsReachable()
    {
        var signedButOdd = Owner(20, "tool.exe", @"C:\Users\Sam\Downloads\tool.exe") with { HasValidSignature = true };

        Assert.Equal(CheckStatus.Warn, PortRules.Judge([Port("TCP", 9000, signedButOdd, "0.0.0.0")], WindowsDirectory).Status);
    }

    [Fact]
    public void Judge_WarnsAboutAnUnsignedProgramThatIsReachable()
    {
        var unsigned = Owner(20, "game.exe", @"C:\Games\game.exe") with { HasValidSignature = false };

        var result = PortRules.Judge([Port("UDP", 27015, unsigned, "0.0.0.0")], WindowsDirectory);

        Assert.Equal(CheckStatus.Warn, result.Status);
        Assert.Contains("without a verified digital signature", result.ResultText);
    }

    [Fact]
    public void Judge_DoesNotWarnAboutAnUnsignedProgramThatOnlyListensOnThisPc()
    {
        var unsigned = Owner(20, "devtool.exe", @"C:\Tools\devtool.exe") with { HasValidSignature = false };

        Assert.Equal(CheckStatus.Pass, PortRules.Judge([Port("TCP", 3000, unsigned, "127.0.0.1")], WindowsDirectory).Status);
    }

    [Fact]
    public void Judge_PassesWhenOtherProgramsOnlyListenOnThisPc_ButStillSaysSo()
    {
        var ports = new[]
        {
            Port("TCP", 135, WindowsService()),
            Port("TCP", 3000, Owner(21, "node.exe", @"C:\Program Files\nodejs\node.exe"), "127.0.0.1", "::1"),
        };

        var result = PortRules.Judge(ports, WindowsDirectory);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("node.exe", result.ResultText);
        Assert.Contains("only accept connections from this PC", result.ResultText);
    }

    [Fact]
    public void Judge_CountsProgramsNotPorts_AndNamesTheFirstFewOnly()
    {
        var ports = Enumerable.Range(1, 5)
            .Select(number => Port("TCP", 9000 + number, Owner(30 + number, $"app{number}.exe", $@"C:\Apps\app{number}.exe")))
            .ToList();

        var result = PortRules.Judge(ports, WindowsDirectory);

        Assert.Contains("5 programs", result.ResultText);
        Assert.Contains("and 2 more", result.ResultText);
    }

    [Fact]
    public void Judge_WarnsAboutAnUnidentifiedProgramHoldingAnExposedPort() =>
        Assert.Equal(CheckStatus.Warn, PortRules.Judge([Port("TCP", 9999, PortRules.UnknownOwner(777))], WindowsDirectory).Status);

    [Fact]
    public void Judge_WarnsWhenWindowsReportedNoPortsAtAll_BecauseThatMeansTheReadingFailed() =>
        Assert.Equal(CheckStatus.Warn, PortRules.Judge([], WindowsDirectory).Status);

    // ── Descriptions ────────────────────────────────────────────────────────

    [Fact]
    public void DescribePurpose_PrefersTheServiceDescription() =>
        Assert.Contains("Serves as the endpoint mapper.", PortRules.DescribePurpose(WindowsService()));

    [Fact]
    public void DescribePurpose_FallsBackToTheFileDescription() =>
        Assert.Equal("Steam Client Bootstrapper", PortRules.DescribePurpose(Owner(1, "steam.exe", @"C:\Steam\steam.exe", "Steam Client Bootstrapper")));

    [Fact]
    public void DescribePurpose_SaysWhenNothingIsPublished() =>
        Assert.Equal("No description published", PortRules.DescribePurpose(Owner(1, "thing.exe", @"C:\x\thing.exe")));

    [Fact]
    public void DescribeProgram_NamesTheProcessAndTheServicesInsideAShellProcess() =>
        Assert.Equal("svchost.exe (process 1200)\nService: Remote Procedure Call (RPC)", PortRules.DescribeProgram(WindowsService()));

    [Fact]
    public void DescribeProgram_CallsTheKernelSystem() =>
        Assert.Equal("System", PortRules.DescribeProgram(Owner(PortRules.SystemProcessId, "System", null)));

    [Fact]
    public void DescribePurpose_ExplainsWhySystemHoldsWebPorts() =>
        Assert.Contains("HTTP.sys", PortRules.DescribePurpose(Owner(PortRules.SystemProcessId, "System", null)));

    // ── Grouping and formatting ports ───────────────────────────────────────

    [Fact]
    public void GroupByProgram_GivesOneEntryPerProcess_NetworkReachableFirstThenByName()
    {
        var steam = Owner(20, "steam.exe", @"C:\Steam\steam.exe");
        var node = Owner(21, "node.exe", @"C:\node\node.exe");
        var alpha = Owner(22, "alpha.exe", @"C:\a\alpha.exe");
        var ports = new[]
        {
            Port("TCP", 3000, node, "127.0.0.1"),
            Port("TCP", 27036, steam, "0.0.0.0"),
            Port("UDP", 27036, steam, "0.0.0.0"),
            Port("TCP", 9, alpha, "127.0.0.1"),
        };

        var programs = PortRules.GroupByProgram(ports);

        Assert.Equal(["steam.exe", "alpha.exe", "node.exe"], programs.Select(program => program.Owner.ProcessName));
        Assert.Equal(2, programs[0].Ports.Count);
        Assert.Equal(2, programs[0].ReachableFromNetwork.Count);
        Assert.Empty(programs[1].ReachableFromNetwork);
    }

    [Fact]
    public void GroupByProgram_KeepsSeparateProcessesOfOneProgramApart()
    {
        var ports = new[] { Port("TCP", 1, Owner(50, "svchost.exe", null)), Port("TCP", 2, Owner(51, "svchost.exe", null)) };

        Assert.Equal(2, PortRules.GroupByProgram(ports).Count);
    }

    [Fact]
    public void FormatPorts_GroupsByProtocolInAscendingOrder()
    {
        var owner = Owner(1, "a.exe", null);
        var ports = new[] { Port("UDP", 53, owner), Port("TCP", 445, owner), Port("TCP", 21, owner), Port("UDP", 5353, owner) };

        Assert.Equal("TCP 21, 445; UDP 53, 5353", PortRules.FormatPorts(ports));
    }

    [Fact]
    public void FormatPorts_JoinsRunsOfThreeOrMoreIntoARange_ButNotPairs()
    {
        var owner = Owner(1, "a.exe", null);
        var ports = new[] { 100, 101, 102, 103, 200, 201, 300 }.Select(number => Port("TCP", number, owner)).ToList();

        Assert.Equal("TCP 100-103, 200, 201, 300", PortRules.FormatPorts(ports));
    }

    [Fact]
    public void FormatPorts_ListsAPortOnceEvenWhenSeenOnSeveralAddresses()
    {
        var owner = Owner(1, "a.exe", null);

        Assert.Equal("TCP 80", PortRules.FormatPorts([Port("TCP", 80, owner, "0.0.0.0"), Port("TCP", 80, owner, "::")]));
    }

    [Fact]
    public void FormatPorts_CapsAProgramThatOpensHundredsAndSaysHowManyAreHidden()
    {
        var owner = Owner(1, "a.exe", null);
        var ports = Enumerable.Range(0, 50).Select(number => Port("UDP", 1000 + number * 2, owner)).ToList();

        var text = PortRules.FormatPorts(ports, maxItemsShown: 10);

        Assert.StartsWith("UDP 1000, 1002", text);
        Assert.EndsWith("and 40 more", text);
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildTables_ListsOneRowPerProgramWithItsPortsSplitByReach()
    {
        var steam = Owner(20, "steam.exe", @"C:\Steam\steam.exe", "Steam", "Valve");
        var ports = new[]
        {
            Port("TCP", 135, WindowsService()),
            Port("TCP", 27036, steam, "0.0.0.0"),
            Port("TCP", 58667, steam, "127.0.0.1"),
            Port("TCP", 3000, Owner(21, "node.exe", @"C:\node\node.exe"), "127.0.0.1"),
        };

        var tables = PortRules.BuildTables(ports, WindowsDirectory);

        Assert.Equal(2, tables.Count);
        var others = tables[0];
        Assert.Contains("other than Windows", others.Title);
        Assert.False(others.StartsCollapsed);
        Assert.Equal([ReportRowEmphasis.Notable, ReportRowEmphasis.Normal], others.Rows.Select(row => row.Emphasis));

        var steamRow = others.Rows[0].Cells;
        Assert.Equal("steam.exe (process 20)", steamRow[0]);
        Assert.Equal("Steam", steamRow[1]);
        Assert.Equal("Valve", steamRow[2]);
        Assert.Equal("TCP 27036", steamRow[3]);
        Assert.Equal("TCP 58667", steamRow[4]);
        Assert.Equal("None", others.Rows[1].Cells[3]);
        Assert.All(others.Rows, row => Assert.Equal(others.Columns.Count, row.Cells.Count));
    }

    [Fact]
    public void BuildTables_OnlyHighlightsReachableProgramsWithoutAVerifiedSignature()
    {
        var signed = Owner(20, "good.exe", @"C:\Apps\good.exe") with { HasValidSignature = true };
        var unsigned = Owner(21, "bad.exe", @"C:\Apps\bad.exe") with { HasValidSignature = false };

        var rows = PortRules.BuildTables([Port("TCP", 1, signed, "0.0.0.0"), Port("TCP", 2, unsigned, "0.0.0.0")], WindowsDirectory)[0].Rows;

        var emphasisByProgram = rows.ToDictionary(row => row.Cells[0].Split(' ')[0], row => row.Emphasis);
        Assert.Equal(ReportRowEmphasis.Normal, emphasisByProgram["good.exe"]);
        Assert.Equal(ReportRowEmphasis.Notable, emphasisByProgram["bad.exe"]);
    }

    [Fact]
    public void BuildTables_FoldsAwayWindowsOwnPortsAndMutesThem()
    {
        var tables = PortRules.BuildTables([Port("TCP", 135, WindowsService())], WindowsDirectory);

        var windowsTable = Assert.Single(tables);
        Assert.True(windowsTable.StartsCollapsed);
        Assert.All(windowsTable.Rows, row => Assert.Equal(ReportRowEmphasis.Muted, row.Emphasis));
        Assert.Equal(windowsTable.Columns.Count, windowsTable.Rows[0].Cells.Count);
    }

    [Fact]
    public void BuildTables_ShowsTheSignatureStateInPlainWords()
    {
        var signed = Owner(20, "good.exe", @"C:\Apps\good.exe") with { HasValidSignature = true };
        var unsigned = Owner(21, "bad.exe", @"C:\Apps\bad.exe") with { HasValidSignature = false };
        var unchecked_ = Owner(22, "new.exe", @"C:\Apps\new.exe");

        var rows = PortRules.BuildTables([Port("TCP", 1, signed), Port("TCP", 2, unsigned), Port("TCP", 3, unchecked_)], WindowsDirectory)[0].Rows;

        var bySignature = rows.ToDictionary(row => row.Cells[0].Split(' ')[0], row => row.Cells[^1]);
        Assert.Equal("Valid", bySignature["good.exe"]);
        Assert.Equal("Not signed, or not trusted", bySignature["bad.exe"]);
        Assert.Equal("Not checked", bySignature["new.exe"]);
    }

    [Fact]
    public void BuildTables_SaysWhenAProgramsLocationCouldNotBeRead()
    {
        var rows = PortRules.BuildTables([Port("TCP", 9999, PortRules.UnknownOwner(777))], WindowsDirectory)[0].Rows;

        Assert.Equal("Couldn't be read", rows[0].Cells[5]);
    }
}
