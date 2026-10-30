using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Real, read-only system diagnostics: CPU/RAM snapshots, free disk space, startup program
/// count (registry Run keys + Startup folder), a Windows service status check, and a rough
/// top-CPU-consumer scan. Every check here only reads - nothing writes to the registry,
/// filesystem, or service control manager, so nothing here can brick anything or trip
/// BitLocker (which reacts to boot/TPM/partition changes, not process/registry reads).
/// </summary>
public static class SystemDiagnosticsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    private static long ToTicks(FILETIME ft) => ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    /// <summary>Blocks briefly (~300ms) to sample a delta - only call from a background thread.</summary>
    public static CheckResult CheckCpuLoad()
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            return new CheckResult(CheckStatus.Warn, "Couldn't read CPU usage on this system.");

        Thread.Sleep(300);

        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            return new CheckResult(CheckStatus.Warn, "Couldn't read CPU usage on this system.");

        var idleDelta = ToTicks(idle2) - ToTicks(idle1);
        var totalDelta = (ToTicks(kernel2) + ToTicks(user2)) - (ToTicks(kernel1) + ToTicks(user1));
        var busy = totalDelta <= 0 ? 0 : 100.0 * (totalDelta - idleDelta) / totalDelta;
        busy = Math.Clamp(busy, 0, 100);

        return busy switch
        {
            < 70 => new CheckResult(CheckStatus.Pass, $"CPU usage is normal ({busy:0}%)."),
            < 90 => new CheckResult(CheckStatus.Warn, $"CPU usage is elevated ({busy:0}%)."),
            _ => new CheckResult(CheckStatus.Bad, $"CPU usage is very high ({busy:0}%)."),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public static CheckResult CheckMemoryUsage()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return new CheckResult(CheckStatus.Warn, "Couldn't read memory usage on this system.");

        var percent = status.dwMemoryLoad;
        return percent switch
        {
            < 80 => new CheckResult(CheckStatus.Pass, $"RAM usage is normal ({percent}%)."),
            < 92 => new CheckResult(CheckStatus.Warn, $"RAM usage is high ({percent}%) - several programs are competing for memory."),
            _ => new CheckResult(CheckStatus.Bad, $"RAM usage is critically high ({percent}%)."),
        };
    }

    /// <summary>Blocks briefly (~500ms) to sample a delta - only call from a background thread.</summary>
    public static CheckResult CheckDiskActivity()
    {
        try
        {
            using var counter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            counter.NextValue(); // first read always returns 0 - needs a primed baseline
            Thread.Sleep(500);
            var percent = Math.Min(counter.NextValue(), 100);

            return percent switch
            {
                < 80 => new CheckResult(CheckStatus.Pass, "Disk activity is normal."),
                < 95 => new CheckResult(CheckStatus.Warn, $"Disk activity is elevated ({percent:0}%)."),
                _ => new CheckResult(CheckStatus.Bad, $"Disk activity is very high ({percent:0}%) - something is hammering the disk."),
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read disk activity on this system.");
        }
    }

    /// <summary>Leftover files under Windows Update's download cache - a common cause of stuck
    /// updates. Read-only (just sums file sizes); the matching fix would need to stop the
    /// Windows Update service before clearing the folder, not wired yet.</summary>
    public static CheckResult CheckStuckUpdateFiles()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            if (!Directory.Exists(path))
                return new CheckResult(CheckStatus.Pass, "No leftover update files found.");

            var sizeInGigabytes = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length) / 1_073_741_824.0;

            return sizeInGigabytes switch
            {
                < 1 => new CheckResult(CheckStatus.Pass, "No significant leftover update files found."),
                < 3 => new CheckResult(CheckStatus.Warn, $"{sizeInGigabytes:0.#} GB of leftover update files found - may be worth clearing."),
                _ => new CheckResult(CheckStatus.Bad, $"{sizeInGigabytes:0.#} GB of leftover update files found - likely causing issues."),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check for leftover update files.");
        }
    }

    public static CheckResult CheckFreeDiskSpace()
    {
        var fixedDrives = DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady).ToList();
        if (fixedDrives.Count == 0)
            return new CheckResult(CheckStatus.Warn, "No fixed drives were found to check.");

        var fullestDrive = fixedDrives.MinBy(drive => drive.AvailableFreeSpace / (double)drive.TotalSize)!;
        var freePercent = 100.0 * fullestDrive.AvailableFreeSpace / fullestDrive.TotalSize;
        var freeGigabytes = fullestDrive.AvailableFreeSpace / 1_073_741_824.0;

        return freePercent switch
        {
            > 15 => new CheckResult(CheckStatus.Pass, "Plenty of free space available."),
            > 5 => new CheckResult(CheckStatus.Warn, $"{fullestDrive.Name} is getting full - only {freeGigabytes:0.#} GB free."),
            _ => new CheckResult(CheckStatus.Bad, $"{fullestDrive.Name} is nearly full - only {freeGigabytes:0.#} GB free."),
        };
    }

    public static CheckResult CheckStartupPrograms()
    {
        var count = CountRegistryRunEntries(Registry.CurrentUser) + CountRegistryRunEntries(Registry.LocalMachine);

        try
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
                count += Directory.GetFiles(startupFolder).Length;
        }
        catch (IOException)
        {
            // Startup folder unavailable - fall back to just the registry count.
        }

        return count switch
        {
            <= 5 => new CheckResult(CheckStatus.Pass, $"{count} program{(count == 1 ? "" : "s")} launching at startup - that's reasonable."),
            <= 10 => new CheckResult(CheckStatus.Warn, $"{count} programs are launching automatically at startup."),
            _ => new CheckResult(CheckStatus.Bad, $"{count} programs are launching automatically at startup."),
        };
    }

    private static int CountRegistryRunEntries(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValueNames().Length ?? 0;
        }
        catch (System.Security.SecurityException)
        {
            return 0;
        }
    }

    public static CheckResult CheckWindowsUpdateService()
    {
        try
        {
            using var service = new ServiceController("wuauserv");
            return service.Status switch
            {
                ServiceControllerStatus.Running => new CheckResult(CheckStatus.Pass, "The update service is running."),
                ServiceControllerStatus.Stopped => new CheckResult(CheckStatus.Warn, "The update service is stopped."),
                _ => new CheckResult(CheckStatus.Warn, $"The update service is currently {service.Status}."),
            };
        }
        catch (InvalidOperationException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the Windows Update service status.");
        }
    }

    /// <summary>Blocks briefly (~400ms) to sample a delta - only call from a background thread.</summary>
    public static CheckResult CheckTopCpuConsumer()
    {
        const int sampleMilliseconds = 400;
        const int significantCpuPercent = 40;

        var ownProcessId = Environment.ProcessId;
        var candidates = Process.GetProcesses()
            .Where(process => process.Id != ownProcessId && !IsSystemProcess(process.ProcessName))
            .ToList();

        var cpuTimeBefore = new Dictionary<int, TimeSpan>();
        foreach (var process in candidates)
        {
            try { cpuTimeBefore[process.Id] = process.TotalProcessorTime; }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { /* elevated or already exited: skip */ }
        }

        Thread.Sleep(sampleMilliseconds);
        var processorCount = Environment.ProcessorCount;

        string? busiestName = null;
        double busiestPercent = 0;
        foreach (var process in candidates)
        {
            if (!cpuTimeBefore.TryGetValue(process.Id, out var cpuBefore)) continue;
            try
            {
                process.Refresh();
                var cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                var cpuPercent = cpuMilliseconds / sampleMilliseconds / processorCount * 100.0;
                if (cpuPercent > busiestPercent)
                {
                    busiestPercent = cpuPercent;
                    busiestName = process.ProcessName;
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { /* elevated or already exited: skip */ }
        }

        foreach (var process in candidates) process.Dispose();

        return busiestPercent > significantCpuPercent
            ? new CheckResult(CheckStatus.Warn, $"\"{busiestName}\" is using significant CPU ({busiestPercent:0}%).")
            : new CheckResult(CheckStatus.Pass, "Nothing unusual running in the background.");
    }

    private static bool IsSystemProcess(string name) =>
        name is "Idle" or "System" or "Registry" or "Secure System" or "Memory Compression";

    /// <summary>
    /// SafeAutoFix - deletes files and folders under the OS-designated temp directory
    /// (<see cref="Path.GetTempPath"/>). Individual locked/in-use items are skipped rather than
    /// failing the whole operation - some in-use temp files is normal, not an error. Matches
    /// the brief's "temp cleanup" whitelist entry; no undo needed, temp files are disposable
    /// by definition.
    /// </summary>
    public static Task<bool> CleanTempFilesAsync() => Task.Run(() =>
    {
        var tempPath = Path.GetTempPath();

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(tempPath)))
        {
            try { File.Delete(file); }
            catch { /* in use or access denied - skip, not a failure */ }
        }

        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(tempPath)))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* not empty/in use - skip, not a failure */ }
        }

        return true;
    });

    private static List<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate().ToList(); }
        catch { return new List<string>(); }
    }
}
