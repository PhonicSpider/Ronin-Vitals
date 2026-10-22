using System.IO;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Scripted symptom/check content ported 1:1 from ronin-vitals-prototype.html's DATA object,
/// so the WPF shell has the same demo content the brief already validated. Results are fixed
/// outcomes for now - swapping ScriptedStatus/ResultText for a real diagnostics engine is
/// tracked separately (see Task #7 and the "Wire up live vitals strip" follow-on work).
/// </summary>
public static class VitalsCatalog
{
    public static IReadOnlyList<Section> Sections { get; } = new List<Section>
    {
        new Section
        {
            Key = SectionKey.Network,
            Label = "Network",
            Description = "Connection, speed, and Wi-Fi",
            AccentBrushKey = "Vitals.Blue.Brush",
            AccentDimBrushKey = "Vitals.Blue.Brush",
            Symptoms = new List<Symptom>
            {
                new Symptom
                {
                    Id = "slow", Title = "My internet feels slow",
                    Subtitle = "Check common causes of sluggish browsing or streaming.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Running a speed test", ScriptedStatus = CheckStatus.Pass, ResultText = "Getting 84 Mb/s down — close to what your plan should give you.", RealCheck = SpeedTestService.CheckDownloadSpeed },
                        new() { Label = "Checking for packet loss", ScriptedStatus = CheckStatus.Pass, ResultText = "No packet loss detected on your connection.", RealCheck = NetworkDiagnosticsService.CheckPacketLoss },
                        new() { Label = "Checking network congestion", ScriptedStatus = CheckStatus.Warn, ResultText = "Your Wi-Fi is sharing a crowded channel with nearby networks.", FixLabel = "See tips to reduce interference", RealCheck = WifiDiagnosticsService.CheckNetworkCongestion,
                            FixInfoText = "This is a router setting, not something on your PC - Vitals can't change it directly. Log into your router's admin page (often 192.168.0.1 or 192.168.1.1) and switch the 2.4GHz channel to 1, 6, or 11 - those don't overlap with each other, unlike most other channels. If your router supports 5GHz, moving to that band usually clears up congestion entirely." },
                        new() { Label = "Checking DNS response time", ScriptedStatus = CheckStatus.Pass, ResultText = "Your DNS is responding quickly.", RealCheck = NetworkDiagnosticsService.CheckDnsResolution },
                        new() { Label = "Checking adapter link speed", ScriptedStatus = CheckStatus.Pass, ResultText = "Your Ethernet connection is running at full speed.", FixLabel = "See what to check", RealCheck = NetworkDiagnosticsService.CheckAdapterLinkSpeed,
                            FixInfoText = "This usually means a bad or old cable, a loose connection, or an older router/switch port negotiating a slower speed. Try a different Ethernet cable (Cat5e or better) and, if possible, a different port on your router or switch. If it's still slow after that, the router/switch or your PC's network card itself may be the problem - a technician can test with proper cable/port testers to pin down which." },
                        new() { Label = "Checking Wi-Fi signal strength", ScriptedStatus = CheckStatus.Warn, ResultText = "Signal is weaker than expected for this room.", FixLabel = "See tips to improve signal", RealCheck = WifiDiagnosticsService.CheckSignalStrengthIfActive,
                            FixInfoText = "Move closer to your router if you can, and check for thick walls, metal, or large appliances between you and it - these block Wi-Fi more than distance alone. Other electronics (microwaves, cordless phones, baby monitors) can also interfere. If signal is still weak in the rooms you actually use, a Wi-Fi extender or mesh system usually fixes it - and if it's weak everywhere, the router itself or its placement may need a technician's look." },
                        new() { Label = "Checking for a proxy connection", ScriptedStatus = CheckStatus.Pass, ResultText = "No proxy server is configured.", FixLabel = "See how to check this", RealCheck = NetworkDiagnosticsService.CheckProxyConfiguration,
                            FixInfoText = "If you didn't set this up yourself and don't recognize needing it, you can turn it off in Settings > Network & Internet > Proxy > turn off \"Use a proxy server\". If this is a work laptop or you use a VPN for a reason, leave it on - the slowdown is likely on that service's end, and your IT department or VPN provider is who to contact, not something to change here." },
                        new() { Label = "Checking connection stability under load", ScriptedStatus = CheckStatus.Pass, ResultText = "Latency stays steady under load - no bufferbloat detected.", FixLabel = "See how to fix this", RealCheck = NetworkDiagnosticsService.CheckLatencyUnderLoad,
                            FixInfoText = "This is called \"bufferbloat\" - your router's outgoing queue fills up under load and delays everything else, including video calls and games, whenever your connection is busy. Many modern routers have a setting called Smart Queue Management (SQM), CAKE, or Quality of Service (QoS) that fixes this - check your router's admin page for it. If your router doesn't support this, upgrading to one that does (many available under $100) usually solves it completely. This is a router setting, not something Vitals can change directly." },
                    }
                },
                new Symptom
                {
                    Id = "nointernet", Title = "I can't get online at all",
                    Subtitle = "Check the basics that stop a connection from working.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking network adapter status", ScriptedStatus = CheckStatus.Pass, ResultText = "Your adapter is enabled and connected.", FixLabel = "See what to check", RealCheck = NetworkDiagnosticsService.CheckAdapterStatus,
                            FixInfoText = "If an adapter shows as turned off, open Network Connections (press Win+R, type ncpa.cpl, press Enter) and right-click it to Enable. If it's enabled but not connected, check that an Ethernet cable is firmly plugged in at both ends, or that Wi-Fi is turned on and connected to a network. If no adapter shows up at all, the network driver may be missing or the hardware may have failed - a technician can check Device Manager and reinstall drivers or test the hardware." },
                        new() { Label = "Checking IP address assignment", ScriptedStatus = CheckStatus.Bad, ResultText = "Your computer doesn't have a valid IP address.", FixLabel = "Renew IP address", RealCheck = NetworkDiagnosticsService.CheckIpAddress, Tier = FixTier.SafeAutoFix, ApplyFix = NetworkDiagnosticsService.RenewIpAsync },
                        new() { Label = "Checking router connection", ScriptedStatus = CheckStatus.Pass, ResultText = "Your router is reachable.", FixLabel = "See what to check", RealCheck = NetworkDiagnosticsService.CheckGatewayReachable,
                            FixInfoText = "Power-cycle your router: unplug it for 30 seconds, then plug it back in and wait a minute for it to fully restart. Check that the cable between your PC and the router (or your Wi-Fi connection to it) is solid. If the router still won't respond after a restart, it may have failed - a technician can test it, or your ISP may need to send a replacement." },
                        new() { Label = "Checking internet access beyond your router", ScriptedStatus = CheckStatus.Pass, ResultText = "The remote server is responding.", FixLabel = "See what this means", RealCheck = NetworkDiagnosticsService.CheckServerReachable,
                            FixInfoText = "If your router check above passed but this one fails, the problem isn't your PC or your home network - it's between your router and the wider internet. Start by power-cycling your modem and router (unplug both for 30 seconds, plug the modem back in first and wait for its lights to stabilize, then the router). If that doesn't fix it, there's likely an outage on your ISP's end - check their status page or call them, since this isn't something Vitals can fix from here." },
                        new() { Label = "Testing DNS resolution", ScriptedStatus = CheckStatus.Warn, ResultText = "Websites are taking unusually long to resolve.", FixLabel = "Flush DNS cache", RealCheck = NetworkDiagnosticsService.CheckDnsResolution, Tier = FixTier.SafeAutoFix, ApplyFix = NetworkDiagnosticsService.FlushDnsAsync },
                    }
                },
                new Symptom
                {
                    Id = "wifidrop", Title = "Wi-Fi keeps dropping",
                    Subtitle = "Check signal strength, interference, and driver health.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Measuring signal strength", ScriptedStatus = CheckStatus.Warn, ResultText = "Signal is weaker than expected for this room.", FixLabel = "Show placement tips", RealCheck = WifiDiagnosticsService.CheckSignalStrength },
                        new() { Label = "Scanning for interference", ScriptedStatus = CheckStatus.Pass, ResultText = "No major interference detected nearby.", RealCheck = WifiDiagnosticsService.CheckNetworkCongestion },
                        new() { Label = "Checking Wi-Fi driver status", ScriptedStatus = CheckStatus.Pass, ResultText = "Driver is up to date.", RealCheck = WifiDiagnosticsService.CheckDriverStatus },
                        new() { Label = "Checking power-saving settings", ScriptedStatus = CheckStatus.Bad, ResultText = "Windows is powering down your Wi-Fi adapter to save battery.", FixLabel = "Disable power saving for Wi-Fi" }, // deliberately left scripted - see WifiDiagnosticsService's doc comment
                    }
                },
                new Symptom
                {
                    Id = "cantreach", Title = "A website or game won't load",
                    Subtitle = "Check whether it's your connection or the destination.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Testing DNS resolution", ScriptedStatus = CheckStatus.Pass, ResultText = "The address resolves correctly.", RealCheck = NetworkDiagnosticsService.CheckDnsResolution },
                        new() { Label = "Checking firewall status", ScriptedStatus = CheckStatus.Bad, ResultText = "Your firewall is blocking the connection.", FixLabel = "Allow this app through firewall", RealCheck = NetworkDiagnosticsService.CheckFirewallStatus },
                        new() { Label = "Testing server reachability", ScriptedStatus = CheckStatus.Pass, ResultText = "The remote server is responding.", RealCheck = NetworkDiagnosticsService.CheckServerReachable },
                        new() { Label = "Clearing stale browser cache", ScriptedStatus = CheckStatus.Pass, ResultText = "Cache looks fine, no action needed." },
                    }
                },
            }
        },
        new Section
        {
            Key = SectionKey.Hardware,
            Label = "Hardware",
            Description = "Performance, heat, and drives",
            AccentBrushKey = "Vitals.Teal.Brush",
            AccentDimBrushKey = "Vitals.Teal.Brush",
            Symptoms = new List<Symptom>
            {
                new Symptom
                {
                    Id = "slowpc", Title = "My computer feels slow",
                    Subtitle = "Check what might be holding your system back.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking CPU load", ScriptedStatus = CheckStatus.Pass, ResultText = "CPU usage is normal.", RealCheck = SystemDiagnosticsService.CheckCpuLoad },
                        new() { Label = "Checking memory usage", ScriptedStatus = CheckStatus.Warn, ResultText = "RAM usage is high — several programs are competing for memory.", FixLabel = "Show what's using RAM", RealCheck = SystemDiagnosticsService.CheckMemoryUsage },
                        new() { Label = "Checking disk activity", ScriptedStatus = CheckStatus.Pass, ResultText = "Disk activity is normal.", RealCheck = SystemDiagnosticsService.CheckDiskActivity },
                        new() { Label = "Checking startup programs", ScriptedStatus = CheckStatus.Bad, ResultText = "12 programs are launching automatically at startup.", FixLabel = "Manage startup programs", RealCheck = SystemDiagnosticsService.CheckStartupPrograms },
                        new() { Label = "Checking for file fragmentation", ScriptedStatus = CheckStatus.Pass, ResultText = "No significant fragmentation found.", RealCheck = DriveFragmentationService.CheckFragmentation },
                    }
                },
                new Symptom
                {
                    Id = "hot", Title = "Fans are loud or it's overheating",
                    Subtitle = "Check temperatures and airflow-related issues.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Reading CPU temperature", ScriptedStatus = CheckStatus.Warn, ResultText = "CPU is running warmer than ideal under load.", FixLabel = "Show cooling tips" },
                        new() { Label = "Checking for thermal throttling", ScriptedStatus = CheckStatus.Pass, ResultText = "No throttling detected right now." },
                        new() { Label = "Checking fan activity", ScriptedStatus = CheckStatus.Pass, ResultText = "Fans are responding normally." },
                    }
                },
                new Symptom
                {
                    Id = "drivefail", Title = "A drive might be failing",
                    Subtitle = "Check drive health before you lose anything.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Reading drive health status", ScriptedStatus = CheckStatus.Bad, ResultText = "Your drive is reporting early signs of wear.", FixLabel = "Back up this drive now", RealCheck = CheckDriveHealth },
                        new() { Label = "Checking free space", ScriptedStatus = CheckStatus.Pass, ResultText = "Plenty of free space available.", RealCheck = SystemDiagnosticsService.CheckFreeDiskSpace },
                        new() { Label = "Checking for read/write errors", ScriptedStatus = CheckStatus.Warn, ResultText = "A few recoverable errors were found.", FixLabel = "Show error details", RealCheck = EventLogDiagnosticsService.CheckDiskErrors },
                    }
                },
                new Symptom
                {
                    Id = "hwall", Title = "Not sure — check everything", IsCatchAll = true,
                    Subtitle = "A general hardware health check, top to bottom.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking CPU health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", RealCheck = SystemDiagnosticsService.CheckCpuLoad },
                        new() { Label = "Checking memory health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", RealCheck = SystemDiagnosticsService.CheckMemoryUsage },
                        new() { Label = "Checking drive health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", RealCheck = CheckDriveHealth },
                        new() { Label = "Checking temperatures", ScriptedStatus = CheckStatus.Pass, ResultText = "All within normal range." },
                    }
                },
            }
        },
        new Section
        {
            Key = SectionKey.Software,
            Label = "Software",
            Description = "Crashes, updates, and apps",
            AccentBrushKey = "Vitals.Yellow.Brush",
            AccentDimBrushKey = "Vitals.Yellow.Brush",
            Symptoms = new List<Symptom>
            {
                new Symptom
                {
                    Id = "crash", Title = "A program keeps crashing",
                    Subtitle = "Check for common causes of repeated crashes.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Reading recent crash logs", ScriptedStatus = CheckStatus.Bad, ResultText = "This program has crashed 4 times in the last day.", FixLabel = "Show crash details", RealCheck = EventLogDiagnosticsService.CheckRecentCrashes },
                        new() { Label = "Checking for corrupt files", ScriptedStatus = CheckStatus.Pass, ResultText = "No corrupted files found." },
                        new() { Label = "Checking for pending updates", ScriptedStatus = CheckStatus.Warn, ResultText = "An update is available that may fix this.", FixLabel = "Update this program" },
                    }
                },
                new Symptom
                {
                    Id = "boot", Title = "Computer won't start up properly",
                    Subtitle = "Check startup, updates, and disk issues.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking startup time", ScriptedStatus = CheckStatus.Warn, ResultText = "Startup is slower than usual.", FixLabel = "Manage startup programs" },
                        new() { Label = "Checking Windows Update status", ScriptedStatus = CheckStatus.Bad, ResultText = "An update is stuck and needs attention.", FixLabel = "Retry Windows Update" },
                        new() { Label = "Running a quick disk check", ScriptedStatus = CheckStatus.Pass, ResultText = "No disk errors found." },
                    }
                },
                new Symptom
                {
                    Id = "resource", Title = "Something's using too much CPU or RAM",
                    Subtitle = "Find out what's eating your resources.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking top CPU consumers", ScriptedStatus = CheckStatus.Warn, ResultText = "One background app is using significant CPU.", FixLabel = "Show top processes", RealCheck = SystemDiagnosticsService.CheckTopCpuConsumer },
                        new() { Label = "Checking background apps", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing unusual running in the background." },
                    }
                },
            }
        },
        new Section
        {
            Key = SectionKey.System,
            Label = "System",
            Description = "Windows, updates, and settings",
            AccentBrushKey = "Vitals.Purple.Brush",
            AccentDimBrushKey = "Vitals.Purple.Brush",
            Symptoms = new List<Symptom>
            {
                new Symptom
                {
                    Id = "update", Title = "Windows won't update",
                    Subtitle = "Check what's blocking the update process.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking Windows Update service", ScriptedStatus = CheckStatus.Pass, ResultText = "The update service is running.", RealCheck = SystemDiagnosticsService.CheckWindowsUpdateService },
                        new() { Label = "Checking available disk space", ScriptedStatus = CheckStatus.Bad, ResultText = "Not enough free space to complete the update.", FixLabel = "Free up disk space", RealCheck = SystemDiagnosticsService.CheckFreeDiskSpace, Tier = FixTier.SafeAutoFix, ApplyFix = SystemDiagnosticsService.CleanTempFilesAsync },
                        new() { Label = "Checking for stuck update files", ScriptedStatus = CheckStatus.Warn, ResultText = "Some leftover update files may be causing issues.", FixLabel = "Clear update cache", RealCheck = SystemDiagnosticsService.CheckStuckUpdateFiles },
                    }
                },
                new Symptom
                {
                    Id = "settings", Title = "Settings keep resetting or acting weird",
                    Subtitle = "Check your system files and user profile.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Verifying system files", ScriptedStatus = CheckStatus.Pass, ResultText = "All core system files check out." },
                        new() { Label = "Checking user profile health", ScriptedStatus = CheckStatus.Warn, ResultText = "Your profile shows signs of minor corruption.", FixLabel = "Show repair options" },
                    }
                },
                new Symptom
                {
                    Id = "general", Title = "General health check", IsCatchAll = true,
                    Subtitle = "A broad look at your system's overall condition.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking system files", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good." },
                        new() { Label = "Checking update status", ScriptedStatus = CheckStatus.Pass, ResultText = "Up to date.", RealCheck = SystemDiagnosticsService.CheckWindowsUpdateService },
                        new() { Label = "Checking recent errors", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing concerning found.", RealCheck = EventLogDiagnosticsService.CheckRecentSystemErrors },
                    }
                },
            }
        },
    };

    public static Section GetSection(SectionKey key) => Sections.First(s => s.Key == key);

    /// <summary>Backs the "Reading drive health status" / "Checking drive health" checks - read-only, see DriveHealthService.</summary>
    private static CheckResult CheckDriveHealth()
    {
        var health = DriveHealthService.GetOverallHealth();
        var partitionCount = DriveInfo.GetDrives().Count(d => d.DriveType == DriveType.Fixed && d.IsReady);

        return health.Level switch
        {
            DriveHealthLevel.Healthy => new CheckResult(CheckStatus.Pass, HealthyDriveMessage(health.TotalDisks, partitionCount)),
            DriveHealthLevel.Warning => new CheckResult(CheckStatus.Warn,
                $"{string.Join(", ", health.ConcerningDisks)} showing early warning signs."),
            DriveHealthLevel.Unhealthy => new CheckResult(CheckStatus.Bad,
                $"{string.Join(", ", health.ConcerningDisks)} reporting a failure warning — back up now."),
            _ => new CheckResult(CheckStatus.Warn, "Couldn't read drive health status on this system."),
        };
    }

    /// <summary>Phrasing scales with disk count so it never reads like it's contradicting the
    /// vitals strip's drive-letter count above it (physical disks vs. drive letters/partitions
    /// are genuinely different numbers - see the checklist entry on the bug this replaced).</summary>
    private static string HealthyDriveMessage(int diskCount, int partitionCount) => diskCount switch
    {
        1 => "Your hard drive has come back healthy across all partitions.",
        2 => $"Both hard drives have come back healthy across {partitionCount} drive partitions.",
        _ => $"All {diskCount} hard drives report healthy across {partitionCount} drive partitions.",
    };

    public static IReadOnlyList<DriverEntry> Drivers { get; } = new List<DriverEntry>
    {
        new() { Vendor = "Windows Update", Name = "Realtek High Definition Audio", CurrentVersion = "6.0.9375.1", LatestVersion = "6.0.9412.1", Method = DriverUpdateMethod.WindowsUpdate },
        new() { Vendor = "Windows Update", Name = "Logitech HID-compliant mouse", CurrentVersion = "2.10.44", LatestVersion = "2.10.61", Method = DriverUpdateMethod.WindowsUpdate },
        new() { Vendor = "NVIDIA", Name = "GeForce RTX 4070 Graphics Driver", CurrentVersion = "551.23", LatestVersion = "566.14", Method = DriverUpdateMethod.Manual },
        new() { Vendor = "NVIDIA", Name = "NVIDIA HD Audio Driver", CurrentVersion = "1.3.42.2", LatestVersion = "1.4.10.1", Method = DriverUpdateMethod.Direct },
        new() { Vendor = "Intel", Name = "Intel Chipset Device Software", CurrentVersion = "10.1.19", LatestVersion = "10.1.19604", Method = DriverUpdateMethod.Manual },
        new() { Vendor = "Intel", Name = "Intel Wi-Fi 6 AX201 Adapter", CurrentVersion = "22.140.0", LatestVersion = "22.230.1", Method = DriverUpdateMethod.Direct },
        new() { Vendor = "MSI", Name = "MSI Mainboard BIOS", CurrentVersion = "7D25v1A", LatestVersion = "7D25v1C", Method = DriverUpdateMethod.Manual },
        new() { Vendor = "HP", Name = "HP OfficeJet Pro 9015 Printer Driver", CurrentVersion = "41.2.1147", LatestVersion = "41.3.1190", Method = DriverUpdateMethod.Direct },
        new() { Vendor = "Brother", Name = "Brother HL-L2350DW Printer Driver", CurrentVersion = "3.9.0", LatestVersion = "4.1.2", Method = DriverUpdateMethod.Direct },
        new() { Vendor = "Beacn", Name = "Beacn Mic Audio Interface", CurrentVersion = "1.4.0", LatestVersion = "1.6.2", Method = DriverUpdateMethod.Direct },
    };
}
