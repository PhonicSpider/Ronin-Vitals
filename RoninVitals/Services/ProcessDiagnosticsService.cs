using System.ComponentModel;
using System.Diagnostics;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only checks of what running programs are doing to memory and responsiveness.</summary>
public static class ProcessDiagnosticsService
{
    private const int TopProgramsShown = 5;

    public static CheckResult CheckTopMemoryConsumer()
    {
        const string techCommand = "Get-Process | Group-Object Name | Select Name,@{n='MB';e={($_.Group | Measure-Object WorkingSet64 -Sum).Sum/1MB}} | Sort-Object MB -Descending";
        var usage = new List<(string Name, long WorkingSetBytes)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { usage.Add((process.ProcessName, process.WorkingSet64)); }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { /* protected or just exited */ }
            }
        }

        var ranked = ProcessRules.GroupByProgram(usage);
        var detail = string.Join("; ", ranked.Take(TopProgramsShown).Select(group => $"{group.Name}: {group.WorkingSetBytes / 1_048_576} MB ({group.InstanceCount})"));
        var totalPhysicalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return ProcessRules.JudgeMemoryUse(ranked, totalPhysicalBytes) with { TechCommand = techCommand, TechDetail = detail };
    }

    public static CheckResult CheckNotResponding()
    {
        const string techCommand = "Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -and -not $_.Responding } | Select Name,Id";
        var frozen = new List<string>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !process.Responding) frozen.Add(process.ProcessName);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { /* protected or just exited */ }
            }
        }

        var detail = frozen.Count == 0 ? "All windowed processes answered." : string.Join(", ", frozen);
        return ProcessRules.JudgeNotResponding(frozen.Distinct().ToList()) with { TechCommand = techCommand, TechDetail = detail };
    }
}
