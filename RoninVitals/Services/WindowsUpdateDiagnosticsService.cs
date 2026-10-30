using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only checks about Windows Update: services, waiting updates, past failures, a pending restart and room to install.</summary>
public static class WindowsUpdateDiagnosticsService
{
    private static readonly string[] UpdateServiceNames = ["wuauserv", "bits", "cryptsvc", "TrustedInstaller"];
    private static readonly TimeSpan UpdateSearchTimeout = TimeSpan.FromSeconds(120);
    private const int FailureWindowDays = 30;
    private const long MillisecondsPerDay = 86_400_000;

    public static CheckResult CheckUpdateServices()
    {
        const string techCommand = "Get-Service wuauserv,bits,cryptsvc,TrustedInstaller | Select Name,Status,StartType";
        try
        {
            var states = UpdateServiceNames.Select(ReadService).ToList();
            var detail = string.Join("; ", states.Select(state => $"{state.Name}: {state.Status} ({state.StartMode})"));
            return UpdateRules.JudgeServices(states) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Update services.", techCommand, CheckText.DescribeException(exception));
        }
    }

    private static ServiceState ReadService(string name)
    {
        using var service = new ServiceController(name);
        return new ServiceState(name, service.Status, service.StartType);
    }

    public static CheckResult CheckPendingRestart()
    {
        const string techCommand = "Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\RebootPending'";
        var reasons = new List<string>();
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) reasons.Add("a Windows component update");
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) reasons.Add("a Windows update");
        var routineFileReplacements = HasPendingFileRenames();

        var detail = $"Update markers: {(reasons.Count == 0 ? "none" : string.Join(", ", reasons))}. Routine file replacements waiting: {routineFileReplacements}.";
        return UpdateRules.SummarizePendingRestart(reasons, routineFileReplacements) with { TechCommand = techCommand, TechDetail = detail };
    }

    private static bool KeyExists(string subKeyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
        return key is not null;
    }

    private static bool HasPendingFileRenames()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
        return key?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
    }

    /// <summary>Asks Windows Update itself what is waiting. Needs the internet and can take a minute, so only call from a background thread.</summary>
    public static CheckResult CheckPendingUpdates()
    {
        const string techCommand = "(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().Search(\"IsInstalled=0 and IsHidden=0 and Type='Software'\").Updates | Select Title";
        const string script =
            "$searcher = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher(); " +
            "$found = $searcher.Search(\"IsInstalled=0 and IsHidden=0 and Type='Software'\"); " +
            "foreach ($update in $found.Updates) { $update.Title }";

        var run = PowerShellRunner.Run(script, UpdateSearchTimeout);
        if (!run.Succeeded)
        {
            var why = run.TimedOut ? "the search took too long" : "Windows Update couldn't be reached";
            return new CheckResult(CheckStatus.Warn, $"Couldn't check for waiting updates ({why}). Check your internet connection and try again.", techCommand, run.Error);
        }

        var titles = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var detail = titles.Length == 0 ? "The search returned no updates." : string.Join("; ", titles);
        return UpdateRules.SummarizePendingUpdates(titles) with { TechCommand = techCommand, TechDetail = detail };
    }

    /// <summary>Failed installs and downloads from the last 30 days, with Windows' own error code so the cause can be named.</summary>
    public static CheckResult CheckUpdateFailures()
    {
        const string log = "Microsoft-Windows-WindowsUpdateClient/Operational";
        const string techCommand = "Get-WinEvent -LogName 'Microsoft-Windows-WindowsUpdateClient/Operational' | Where-Object Id -in 20,31";
        var query = $"*[System[(EventID=20 or EventID=31) and TimeCreated[timediff(@SystemTime) <= {FailureWindowDays * MillisecondsPerDay}]]]";

        try
        {
            var failures = EventLogDiagnosticsService.ReadEvents(log, query)
                .Select(logged => new UpdateFailure(
                    ErrorCode: UpdateRules.ToHexCode(logged.Fields.Count > 0 ? logged.Fields[0] : "unknown"),
                    UpdateTitle: logged.Fields.Count > 1 ? logged.Fields[1] : "an update"))
                .ToList();

            var detail = failures.Count == 0
                ? "No install (20) or download (31) failures logged."
                : string.Join("; ", failures.Take(5).Select(failure => $"{failure.ErrorCode} {failure.UpdateTitle}"));
            return UpdateRules.SummarizeFailures(failures) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (EventLogNotFoundException)
        {
            return new CheckResult(CheckStatus.Pass, "Windows hasn't recorded any update history on this PC.", techCommand, $"Log '{log}' not found.");
        }
        catch (Exception exception) when (exception is EventLogException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Update history.", techCommand, CheckText.DescribeException(exception));
        }
    }

    /// <summary>Updates install onto the drive Windows lives on, so that drive's free space matters more than the fullest drive's.</summary>
    public static CheckResult CheckSystemDriveSpace()
    {
        const string techCommand = "Get-PSDrive C | Select Used,Free";
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory)!;
            var drive = new DriveInfo(root);
            var freeGigabytes = drive.AvailableFreeSpace / 1_073_741_824.0;
            var detail = $"{drive.Name} {freeGigabytes:0.0} GB free of {drive.TotalSize / 1_073_741_824.0:0.0} GB.";
            return UpdateRules.JudgeSystemDriveSpace(drive.Name, freeGigabytes) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read free space on the Windows drive.", techCommand, CheckText.DescribeException(exception));
        }
    }
}
