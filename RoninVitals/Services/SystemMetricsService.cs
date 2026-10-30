using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace RoninVitals.Services;

/// <summary>One live reading for a vitals-strip tile: 0-100 for the sparkline gauge, plus the display text.</summary>
public sealed record VitalSample(double GaugeValue, string DisplayText);

/// <summary>
/// Real system metric sampling for the vitals strip - CPU/RAM via kernel32 P/Invoke (no extra
/// NuGet dependency), network via NetworkInterface byte-counter deltas, disk via DriveInfo.
/// No SMART/health data yet - that is diagnostics-engine scope (see VitalsCatalog and the
/// build checklist), not vitals-strip scope.
/// </summary>
public sealed class SystemMetricsService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

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
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static long ToTicks(FILETIME fileTime) => ((long)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;

    private long _lastIdleTicks;
    private long _lastTotalTicks;
    private bool _cpuWarm;

    private DateTime _lastNetSampleAt;
    private long _lastNetBytes;
    private bool _netWarm;

    public VitalSample SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return new VitalSample(0, "n/a");

        var idleTicks = ToTicks(idle);
        var totalTicks = ToTicks(kernel) + ToTicks(user);

        if (!_cpuWarm)
        {
            _lastIdleTicks = idleTicks;
            _lastTotalTicks = totalTicks;
            _cpuWarm = true;
            return new VitalSample(0, "0%");
        }

        var idleDelta = idleTicks - _lastIdleTicks;
        var totalDelta = totalTicks - _lastTotalTicks;
        _lastIdleTicks = idleTicks;
        _lastTotalTicks = totalTicks;

        var busyPercent = totalDelta <= 0 ? 0 : 100.0 * (totalDelta - idleDelta) / totalDelta;
        busyPercent = Math.Clamp(busyPercent, 0, 100);
        return new VitalSample(busyPercent, $"{busyPercent:0}%");
    }

    public VitalSample SampleRam()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return new VitalSample(0, "n/a");

        double percent = status.dwMemoryLoad;
        return new VitalSample(percent, $"{percent:0}%");
    }

    public VitalSample SampleNetwork()
    {
        var now = DateTime.UtcNow;
        long totalBytes = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var statistics = adapter.GetIPv4Statistics();
                totalBytes += statistics.BytesReceived + statistics.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // Interface without usable IPv4 stats - skip it.
            }
        }

        if (!_netWarm)
        {
            _lastNetBytes = totalBytes;
            _lastNetSampleAt = now;
            _netWarm = true;
            return new VitalSample(0, "0.0 Mb/s");
        }

        var elapsedSeconds = (now - _lastNetSampleAt).TotalSeconds;
        var bytesSinceLastSample = Math.Max(0, totalBytes - _lastNetBytes);
        _lastNetBytes = totalBytes;
        _lastNetSampleAt = now;

        var megabitsPerSecond = elapsedSeconds <= 0 ? 0 : bytesSinceLastSample * 8.0 / 1_000_000.0 / elapsedSeconds;

        // Scaled onto the 0-100 sparkline: 50 Mb/s reads as "full". A display choice, not link capacity.
        const double fullGaugeMegabitsPerSecond = 50.0;
        var gauge = Math.Clamp(megabitsPerSecond / fullGaugeMegabitsPerSecond * 100.0, 0, 100);
        return new VitalSample(gauge, $"{megabitsPerSecond:0.0} Mb/s");
    }

    /// <summary>"Days:Hours:Minutes:Seconds", zero-padded (e.g. 21 days, 10 hours, 52 minutes, 33 seconds is
    /// "21:10:52:33"). The seconds field makes it a live ticking clock.</summary>
    public static string FormatUptime(TimeSpan uptime) =>
        $"{(int)uptime.TotalDays:00}:{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";

    /// <summary>Time since Windows last started, from the system tick counter (the same source Task
    /// Manager's "Up time" uses). With Fast Startup on, "Shut down" does not reset it - only a
    /// restart does - which matches what Task Manager shows.</summary>
    public string SampleUptimeText() => FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));

    public IReadOnlyList<string> GetFixedDriveNames() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.Name.TrimEnd('\\'))
            .ToList();

    public VitalSample SampleDrive(string driveName)
    {
        try
        {
            var drive = new DriveInfo(driveName);
            if (!drive.IsReady) return new VitalSample(0, "not ready");

            var usedPercent = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
            usedPercent = Math.Clamp(usedPercent, 0, 100);
            return new VitalSample(usedPercent, $"{usedPercent:0}% used");
        }
        catch (IOException)
        {
            return new VitalSample(0, "unavailable");
        }
    }
}
