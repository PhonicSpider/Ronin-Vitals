using System.Net;
using System.Text.Json;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>One socket Windows reports as open: listening (TCP) or bound (UDP).</summary>
public sealed record PortListener(string Protocol, string Address, int Port, int ProcessId);

/// <summary>A Windows service that runs inside the process holding a port (many services share one svchost.exe).</summary>
public sealed record HostedService(string Name, string DisplayName, string? Description, string? ExecutablePath = null);

/// <summary>The program holding a port. <paramref name="HasValidSignature"/> is null when it wasn't checked or couldn't be.</summary>
public sealed record PortOwner(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string? FileDescription,
    string? Company,
    bool? HasValidSignature,
    IReadOnlyList<HostedService> Services);

/// <summary>One open port held by one program, with its IPv4 and IPv6 addresses merged into one row.</summary>
public sealed record OpenPort(string Protocol, int Port, IReadOnlyList<string> Addresses, PortOwner Owner)
{
    public bool IsReachableFromNetwork => Addresses.Any(address => !PortRules.IsLoopback(address));
}

/// <summary>Every open port one program holds.</summary>
public sealed record ProgramPorts(PortOwner Owner, IReadOnlyList<OpenPort> Ports)
{
    public IReadOnlyList<OpenPort> ReachableFromNetwork => Ports.Where(port => port.IsReachableFromNetwork).ToList();
    public IReadOnlyList<OpenPort> ThisPcOnly => Ports.Where(port => !port.IsReachableFromNetwork).ToList();
}

/// <summary>
/// What counts as a Windows default open port, and how the open ports are judged and tabulated, kept apart from the code
/// that reads the machine so it can be tested with sample data. "Windows default" means the port is held by Windows itself:
/// the System process or a program that lives in Windows' own program folders. A program's name alone is never trusted,
/// because malware can call itself svchost.exe; its location is what is checked.
/// </summary>
public static class PortRules
{
    public const int IdleProcessId = 0;
    public const int SystemProcessId = 4;

    private static readonly string[] WindowsProgramFolders = ["System32", "SysWOW64", "SystemApps"];

    public static PortOwner UnknownOwner(int processId) =>
        new(processId, "Unknown program", ExecutablePath: null, FileDescription: null, Company: null, HasValidSignature: null, Services: []);

    /// <summary>Reads the JSON from <c>ConvertTo-Json</c>, which is an object for one socket, an array for several and nothing for none.</summary>
    public static IReadOnlyList<PortListener> ParseListeners(string json)
    {
        var listeners = new List<PortListener>();
        if (string.IsNullOrWhiteSpace(json)) return listeners;

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];

        foreach (var item in items.Where(candidate => candidate.ValueKind == JsonValueKind.Object))
        {
            if (!item.TryGetProperty("LocalPort", out var port) || !port.TryGetInt32(out var portNumber)) continue;
            if (!item.TryGetProperty("OwningProcess", out var owner) || !owner.TryGetInt32(out var processId)) continue;

            listeners.Add(new PortListener(
                Protocol: ReadText(item, "Protocol") ?? "TCP",
                Address: ReadText(item, "LocalAddress") ?? "unknown",
                Port: portNumber,
                ProcessId: processId));
        }
        return listeners;
    }

    private static string? ReadText(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static bool IsLoopback(string address)
    {
        var withoutZone = address.Split('%', 2)[0];
        return IPAddress.TryParse(withoutZone, out var parsed) && IPAddress.IsLoopback(parsed);
    }

    public static bool IsWindowsDefault(PortOwner owner, string windowsDirectory)
    {
        if (owner.ProcessId is IdleProcessId or SystemProcessId) return true;
        if (string.IsNullOrWhiteSpace(owner.ExecutablePath)) return false;

        var path = owner.ExecutablePath.Replace('/', '\\');
        var root = windowsDirectory.TrimEnd('\\', '/');
        return WindowsProgramFolders.Any(folder => path.StartsWith($@"{root}\{folder}\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Merges sockets that differ only by address (127.0.0.1 and ::1, say) into one row per program and port.</summary>
    public static IReadOnlyList<OpenPort> Group(IEnumerable<PortListener> listeners, Func<int, PortOwner> findOwner) =>
        listeners
            .GroupBy(listener => (listener.Protocol, listener.Port, listener.ProcessId))
            .Select(group => new OpenPort(
                group.Key.Protocol,
                group.Key.Port,
                group.Select(listener => listener.Address).Distinct().Order(StringComparer.Ordinal).ToList(),
                findOwner(group.Key.ProcessId)))
            .OrderBy(port => port.Port)
            .ThenBy(port => port.Protocol, StringComparer.Ordinal)
            .ToList();

    /// <summary>One entry per running program (not per port), with programs the network can reach first.</summary>
    public static IReadOnlyList<ProgramPorts> GroupByProgram(IEnumerable<OpenPort> ports) =>
        ports
            .GroupBy(port => port.Owner.ProcessId)
            .Select(group => new ProgramPorts(group.First().Owner, group.ToList()))
            .OrderByDescending(program => program.ReachableFromNetwork.Count > 0)
            .ThenBy(program => program.Owner.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(program => program.Owner.ProcessId)
            .ToList();

    /// <summary>"TCP 21, 135; UDP 53, 5353-5355": ports in ascending order, runs of three or more joined into a range, and a cap for programs that open hundreds.</summary>
    public static string FormatPorts(IEnumerable<OpenPort> ports, int maxItemsShown = 30)
    {
        var parts = new List<string>();
        var shownItems = 0;
        var hiddenPorts = 0;

        foreach (var protocolGroup in ports.GroupBy(port => port.Protocol).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var items = new List<string>();
            foreach (var (first, last) in JoinRuns(protocolGroup.Select(port => port.Port).Distinct().Order().ToList()))
            {
                if (shownItems >= maxItemsShown) { hiddenPorts += last - first + 1; continue; }
                items.Add(first == last ? first.ToString() : $"{first}-{last}");
                shownItems++;
            }
            if (items.Count > 0) parts.Add($"{protocolGroup.Key} {string.Join(", ", items)}");
        }

        var text = string.Join("; ", parts);
        return hiddenPorts > 0 ? $"{text} and {hiddenPorts} more" : text;
    }

    /// <summary>Joins runs of three or more consecutive numbers; shorter runs stay as single numbers.</summary>
    private static IEnumerable<(int First, int Last)> JoinRuns(IReadOnlyList<int> sortedPorts)
    {
        var index = 0;
        while (index < sortedPorts.Count)
        {
            var end = index;
            while (end + 1 < sortedPorts.Count && sortedPorts[end + 1] == sortedPorts[end] + 1) end++;

            if (end - index >= 2) yield return (sortedPorts[index], sortedPorts[end]);
            else for (var single = index; single <= end; single++) yield return (sortedPorts[single], sortedPorts[single]);
            index = end + 1;
        }
    }

    // ── Wording for one program ─────────────────────────────────────────────────────────────

    /// <summary>The program's name and process number (the number tells apart several copies of one program), plus any Windows services inside it.</summary>
    public static string DescribeProgram(PortOwner owner)
    {
        if (owner.ProcessId is IdleProcessId or SystemProcessId) return "System";

        var name = $"{owner.ProcessName} (process {owner.ProcessId})";
        return owner.Services.Count == 0
            ? name
            : $"{name}\nService: {string.Join(", ", owner.Services.Select(service => service.DisplayName))}";
    }

    /// <summary>What the program says it is: its service description if it runs as a service, otherwise its file description.</summary>
    public static string DescribePurpose(PortOwner owner)
    {
        if (owner.ProcessId is IdleProcessId or SystemProcessId)
            return "The Windows kernel. Runs file sharing and Windows' built-in web listener (HTTP.sys), which other programs ask Windows to open ports through.";

        var serviceDescriptions = owner.Services
            .Where(service => !string.IsNullOrWhiteSpace(service.Description))
            .Select(service => $"{service.DisplayName}: {service.Description!.Trim()}")
            .ToList();
        if (serviceDescriptions.Count > 0) return string.Join("\n", serviceDescriptions);

        return string.IsNullOrWhiteSpace(owner.FileDescription) ? "No description published" : owner.FileDescription.Trim();
    }

    private static string DescribeSignature(bool? hasValidSignature) => hasValidSignature switch
    {
        true => "Valid",
        false => "Not signed, or not trusted",
        null => "Not checked",
    };

    // ── Judging and tabulating ──────────────────────────────────────────────────────────────

    public static CheckResult Judge(IReadOnlyList<OpenPort> ports, string windowsDirectory)
    {
        if (ports.Count == 0)
            return new CheckResult(CheckStatus.Warn, "Windows didn't list any open ports, which is unusual for a working PC. The reading may have been blocked.");

        var others = ports.Where(port => !IsWindowsDefault(port.Owner, windowsDirectory)).ToList();
        if (others.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"Only Windows itself is listening for connections ({ports.Count} {Plural(ports.Count, "port", "ports")}). Nothing from other programs is open.");

        var programs = others.Select(port => port.Owner.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var summary = $"{programs.Count} {Plural(programs.Count, "program", "programs")} other than Windows {Plural(programs.Count, "has", "have")} " +
                      $"{others.Count} {Plural(others.Count, "port", "ports")} open (for example {Examples(programs)}).";

        var exposedPrograms = others.Where(port => port.IsReachableFromNetwork).Select(port => port.Owner.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (exposedPrograms.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"{summary} They only accept connections from this PC, not from your network. The report lists each one.");

        var needAttention = others.Where(NeedsAttention).Select(port => port.Owner.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (needAttention.Count == 0)
            return new CheckResult(CheckStatus.Pass,
                $"{summary} {exposedPrograms.Count} of them accept connections from your network, and each has a valid digital signature from its maker. The report lists each one.");

        return new CheckResult(CheckStatus.Warn,
            $"{summary} {needAttention.Count} {Plural(needAttention.Count, "program", "programs")} can be reached from other devices on your network without a verified digital signature " +
            $"(for example {Examples(needAttention)}). Apps you installed on purpose are often fine; the report shows where each one lives so you can judge.");
    }

    /// <summary>Worth a closer look: reachable from other devices, and either not verifiably signed or running from a folder malware favours.</summary>
    public static bool NeedsAttention(OpenPort port) =>
        port.IsReachableFromNetwork && (port.Owner.HasValidSignature != true || SuspiciousFileRules.IsRiskyLaunchLocation(port.Owner.ExecutablePath));

    public static IReadOnlyList<ReportTable> BuildTables(IReadOnlyList<OpenPort> ports, string windowsDirectory)
    {
        var tables = new List<ReportTable>();

        var others = GroupByProgram(ports.Where(port => !IsWindowsDefault(port.Owner, windowsDirectory)));
        if (others.Count > 0)
        {
            tables.Add(new ReportTable(
                Title: "Programs other than Windows that have ports open",
                Columns: ["Program", "What it is", "Company", "Open to your network", "This PC only", "Location", "Digital signature"],
                Rows: others.Select(program => new ReportRow(
                    [
                        DescribeProgram(program.Owner),
                        DescribePurpose(program.Owner),
                        program.Owner.Company ?? "Unknown",
                        DescribePorts(program.ReachableFromNetwork),
                        DescribePorts(program.ThisPcOnly),
                        program.Owner.ExecutablePath ?? "Couldn't be read",
                        DescribeSignature(program.Owner.HasValidSignature),
                    ],
                    program.Ports.Any(NeedsAttention) ? ReportRowEmphasis.Notable : ReportRowEmphasis.Normal)).ToList(),
                Note: "\"Open to your network\" means the program accepts connections from other devices. Windows Firewall may still block them, " +
                      "and apps you installed on purpose (games, chat, media servers, device software) often do this. Highlighted rows are reachable from your network " +
                      "and don't have a verified digital signature, or run from an unusual folder. A program you don't recognise is the thing to look into."));
        }

        var windows = GroupByProgram(ports.Where(port => IsWindowsDefault(port.Owner, windowsDirectory)));
        if (windows.Count > 0)
        {
            tables.Add(new ReportTable(
                Title: "Windows' own open ports",
                Columns: ["Program", "What it is", "Open to your network", "This PC only"],
                Rows: windows.Select(program => new ReportRow(
                    [DescribeProgram(program.Owner), DescribePurpose(program.Owner), DescribePorts(program.ReachableFromNetwork), DescribePorts(program.ThisPcOnly)],
                    ReportRowEmphasis.Muted)).ToList(),
                Note: "Held by Windows itself (the System process or programs in Windows' own folders). These are normal.",
                StartsCollapsed: true));
        }
        return tables;
    }

    private static string DescribePorts(IReadOnlyList<OpenPort> ports) => ports.Count == 0 ? "None" : FormatPorts(ports);
}
