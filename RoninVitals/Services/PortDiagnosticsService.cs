using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>
/// Read-only look at which programs are listening for connections. It lists open ports and what holds them; it never closes
/// a port, stops a program or changes the firewall. What counts as a Windows default is decided in <see cref="PortRules"/>.
/// </summary>
public static class PortDiagnosticsService
{
    private static readonly TimeSpan ListenerQueryTimeout = TimeSpan.FromSeconds(60);

    private const string TechCommand = "Get-NetTCPConnection -State Listen; Get-NetUDPEndpoint   (then the program and any Windows service behind each owning process)";

    // -InputObject keeps a single result as a one-item array, which piping would turn into a bare object.
    private const string ListenerScript =
        "$tcp = Get-NetTCPConnection -State Listen | Select-Object @{n='Protocol';e={'TCP'}}, LocalAddress, LocalPort, OwningProcess; " +
        "$udp = Get-NetUDPEndpoint | Select-Object @{n='Protocol';e={'UDP'}}, LocalAddress, LocalPort, OwningProcess; " +
        "ConvertTo-Json -Compress -InputObject @(@($tcp) + @($udp))";

    private sealed record RunningProgram(string Name, string? ExecutablePath);

    public static CheckResult CheckOpenPorts()
    {
        var query = PowerShellRunner.Run(ListenerScript, ListenerQueryTimeout);
        if (!query.Succeeded)
        {
            var reason = query.TimedOut ? "Timed out reading the open ports." : FirstLine(query.Error);
            return new CheckResult(CheckStatus.Warn, "Couldn't read the list of open ports from Windows.", TechCommand, reason);
        }

        try
        {
            var listeners = PortRules.ParseListeners(query.Output);
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var owners = ReadOwners(listeners.Select(listener => listener.ProcessId).Distinct().ToList(), windowsDirectory);
            var ports = PortRules.Group(listeners, processId => owners.GetValueOrDefault(processId) ?? PortRules.UnknownOwner(processId));

            return PortRules.Judge(ports, windowsDirectory) with
            {
                TechCommand = TechCommand,
                TechDetail = DescribeForTechnician(ports, windowsDirectory),
                Tables = PortRules.BuildTables(ports, windowsDirectory),
            };
        }
        catch (Exception exception) when (exception is JsonException or ManagementException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish checking the open ports.", TechCommand, DescribeException(exception));
        }
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private static string DescribeForTechnician(IReadOnlyList<OpenPort> ports, string windowsDirectory)
    {
        var others = ports.Where(port => !PortRules.IsWindowsDefault(port.Owner, windowsDirectory)).ToList();
        var summary = $"{ports.Count} open ports; {ports.Count - others.Count} held by Windows, {others.Count} by other programs.";
        return others.Count == 0
            ? summary
            : summary + " Other programs: " + string.Join(" | ", PortRules.GroupByProgram(others).Select(program =>
                $"{program.Owner.ProcessName} pid {program.Owner.ProcessId} ({program.Owner.ExecutablePath ?? "location unknown"}): {PortRules.FormatPorts(program.Ports)}"));
    }

    // ── Who holds each port ─────────────────────────────────────────────────────────────────

    private static Dictionary<int, PortOwner> ReadOwners(IReadOnlyList<int> processIds, string windowsDirectory)
    {
        var programs = ReadRunningPrograms();
        var services = ReadServicesByProcess();
        var owners = new Dictionary<int, PortOwner>();

        foreach (var processId in processIds)
        {
            if (processId is PortRules.IdleProcessId or PortRules.SystemProcessId)
            {
                owners[processId] = PortRules.UnknownOwner(processId) with { ProcessName = "System" };
                continue;
            }
            if (!programs.TryGetValue(processId, out var program)) continue; // it closed since the ports were read: left to UnknownOwner

            // Without administrator rights WMI hides where system programs live, so fall back to the narrower Win32 query,
            // then to the path of the service the program hosts (which is how a service's own program is registered).
            var hosted = services.TryGetValue(processId, out var hostedServices) ? hostedServices : [];
            var path = program.ExecutablePath ?? ProcessImagePath.Find(processId) ?? hosted.Select(service => service.ExecutablePath).FirstOrDefault(servicePath => servicePath is not null);

            var (description, company) = ReadVersionInfo(path);
            var owner = new PortOwner(processId, program.Name, path, description, company, HasValidSignature: null, hosted);

            // The signature check reads the file, so it's only worth doing for programs that aren't Windows' own.
            owners[processId] = owner.ExecutablePath is not null && !PortRules.IsWindowsDefault(owner, windowsDirectory)
                ? owner with { HasValidSignature = AuthenticodeVerifier.Verify(owner.ExecutablePath) == AuthenticodeVerifier.ValidSignature }
                : owner;
        }
        return owners;
    }

    private static Dictionary<int, RunningProgram> ReadRunningPrograms()
    {
        var programs = new Dictionary<int, RunningProgram>();
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath FROM Win32_Process");
        foreach (ManagementBaseObject process in searcher.Get())
        {
            using (process)
            {
                var processId = Convert.ToInt32(process["ProcessId"]);
                programs[processId] = new RunningProgram(process["Name"] as string ?? "Unknown program", process["ExecutablePath"] as string);
            }
        }
        return programs;
    }

    private static Dictionary<int, IReadOnlyList<HostedService>> ReadServicesByProcess()
    {
        var byProcess = new Dictionary<int, List<HostedService>>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, Description, PathName, ProcessId FROM Win32_Service WHERE ProcessId > 0");
        foreach (ManagementBaseObject service in searcher.Get())
        {
            using (service)
            {
                var processId = Convert.ToInt32(service["ProcessId"]);
                var name = service["Name"] as string ?? "unknown";
                var servicePath = SuspiciousFileRules.ExtractExecutablePath(service["PathName"] as string);
                if (!byProcess.TryGetValue(processId, out var list)) byProcess[processId] = list = [];
                list.Add(new HostedService(name, service["DisplayName"] as string ?? name, service["Description"] as string, servicePath));
            }
        }
        return byProcess.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<HostedService>)entry.Value);
    }

    private static (string? Description, string? Company) ReadVersionInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, null);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var description = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            return (description, info.CompanyName);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }
}
