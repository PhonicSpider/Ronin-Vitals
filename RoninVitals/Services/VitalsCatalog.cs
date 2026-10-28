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
                        new() { Label = "Measuring signal strength", ScriptedStatus = CheckStatus.Warn, ResultText = "Signal is weaker than expected for this room.", FixLabel = "Show placement tips", RealCheck = WifiDiagnosticsService.CheckSignalStrength,
                            FixInfoText = "Move closer to your router if you can, and check for thick walls, metal, mirrors, or large appliances between you and it - these block Wi-Fi more than distance alone. Keep the router out in the open, off the floor and away from the microwave or cordless phones. If signal is still weak in the rooms you actually use, a Wi-Fi extender or mesh system usually fixes it; if it's weak everywhere, the router itself or its placement may need a technician's look." },
                        new() { Label = "Scanning for interference", ScriptedStatus = CheckStatus.Pass, ResultText = "No major interference detected nearby.", FixLabel = "See tips to reduce interference", RealCheck = WifiDiagnosticsService.CheckChannelInterference,
                            FixInfoText = "This is a router setting, not something on your PC - Vitals can't change it directly. Log into your router's admin page (often 192.168.0.1 or 192.168.1.1) and switch the 2.4GHz channel to 1, 6, or 11 - those don't overlap with each other, unlike most other channels. If your router supports 5GHz, moving your devices to that band usually clears up interference entirely." },
                        new() { Label = "Checking for recent Wi-Fi disconnects", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows hasn't logged any Wi-Fi disconnects in the last 24 hours.", FixLabel = "See what this means", RealCheck = EventLogDiagnosticsService.CheckWifiDisconnects,
                            FixInfoText = "Windows keeps a log of every time your Wi-Fi disconnected, which is the clearest proof the drops are real. A few entries can just be your PC sleeping or switching networks. Many entries mean a real problem: work down the other checks on this card (signal, interference, power saving, driver) and the stability test below to find which side of the router it's on. If it keeps happening after you've tried them, a technician can look at the router and your adapter." },
                        new() { Label = "Testing connection stability", ScriptedStatus = CheckStatus.Pass, ResultText = "All 10 test packets reached both your router and the internet - the connection held steady.", FixLabel = "See what to do about it", RealCheck = NetworkDiagnosticsService.CheckLinkStability,
                            FixInfoText = "If packets were lost reaching your router, the problem is between your PC and the router: improve the Wi-Fi signal (move closer, or use a cable), turn off Wi-Fi power saving, and update the Wi-Fi driver. If your router answered every test but packets to the internet were lost, the problem is beyond your router: power-cycle your modem and router (unplug both for 30 seconds, modem first), and if drops continue, contact your internet provider - Vitals can't fix that part." },
                        new() { Label = "Checking Wi-Fi driver status", ScriptedStatus = CheckStatus.Pass, ResultText = "Driver is up to date.", FixLabel = "See how to update it", RealCheck = WifiDiagnosticsService.CheckDriverStatus,
                            FixInfoText = "An old Wi-Fi driver is a common cause of random drops, especially after a Windows update. Use \"Check for driver updates\" on the Home screen, or download the newest Wi-Fi driver from your PC or laptop maker's support page (or from Intel, Realtek, or MediaTek for the adapter itself). If you just updated and the drops started afterward, a technician can roll the driver back." },
                        new() { Label = "Checking power-saving settings", ScriptedStatus = CheckStatus.Warn, ResultText = "Windows lets your Wi-Fi adapter power down to save energy.", FixLabel = "Turn off Wi-Fi power saving", RealCheck = WifiDiagnosticsService.CheckPowerSaving, Tier = FixTier.SafeAutoFix, ApplyFix = WifiDiagnosticsService.DisableWirelessPowerSavingAsync, UndoFix = WifiDiagnosticsService.RestoreWirelessPowerSavingAsync },
                    }
                },
                new Symptom
                {
                    Id = "cantreach", Title = "A website or game won't load",
                    Subtitle = "Check whether it's your connection or the destination.",
                    Checks = new List<CheckDefinition>
                    {
                        // Ordered to answer "is it me or them?" first, then rule out the local causes that quietly
                        // break just some sites: DNS, the clock, a proxy, the hosts file, a firewall rule, a stale cache.
                        new() { Label = "Checking whether popular sites and game services load", ScriptedStatus = CheckStatus.Pass, ResultText = "All popular sites and game services loaded.", FixLabel = "See what this means", RealCheck = WebAccessDiagnosticsService.CheckPopularSites,
                            FixInfoText = "This tries about ten well-known sites and game services (Google, Microsoft, Steam, Epic, Xbox, Battle.net, Riot, EA, Discord). If NOTHING loads, the problem is your connection or DNS, so work down the other checks here and on \"I can't get online at all\". If only a few fail while the rest load, those services are most likely having trouble on their end: check their status pages (status.epicgames.com, discordstatus.com, support.xbox.com/en-US/xbox-live-status) and wait it out, since nothing on your PC needs fixing. If it's only one service that fails for you but its status page says it's fine, a local block (the hosts file or firewall checks below) is the likely cause. If the check says your network wants you to sign in, open a browser and complete the sign-in page first." },
                        new() { Label = "Comparing your DNS to a public one", ScriptedStatus = CheckStatus.Pass, ResultText = "Your PC's DNS looked up every test site correctly.", FixLabel = "How to switch DNS", RealCheck = WebAccessDiagnosticsService.CheckDnsVersusPublic,
                            FixInfoText = "DNS turns a site name into an address, and a bad or overloaded DNS server (often your internet provider's) is a very common reason one site fails while others work. To switch to a public one: Settings > Network & internet > pick Wi-Fi or Ethernet > your connection > DNS server assignment > Edit > Manual > turn on IPv4 > Preferred DNS 1.1.1.1 and Alternate DNS 8.8.8.8 > Save. Then try the site again. On a work or school network, leave DNS alone and ask your IT department, since they may rely on their own DNS." },
                        new() { Label = "Checking your clock is right", ScriptedStatus = CheckStatus.Pass, ResultText = "Your PC's clock is accurate.", FixLabel = "Re-sync the clock", RealCheck = WebAccessDiagnosticsService.CheckSystemClock, Tier = FixTier.SafeAutoFix, ApplyFix = WebAccessDiagnosticsService.SyncClockAsync },
                        new() { Label = "Checking for a proxy connection", ScriptedStatus = CheckStatus.Pass, ResultText = "No proxy server is configured.", FixLabel = "See how to check this", RealCheck = NetworkDiagnosticsService.CheckProxyConfiguration,
                            FixInfoText = "If you didn't set this up yourself and don't recognize needing it, you can turn it off in Settings > Network & internet > Proxy > turn off \"Use a proxy server\". A proxy that's down or misconfigured makes sites fail even though your connection is fine. If this is a work laptop or you use a VPN for a reason, leave it on and ask your IT department or VPN provider instead." },
                        new() { Label = "Checking your hosts file for blocked sites", ScriptedStatus = CheckStatus.Pass, ResultText = "Your hosts file has no custom entries.", FixLabel = "How to review it", RealCheck = WebAccessDiagnosticsService.CheckHostsFile,
                            FixInfoText = "The hosts file is a list on your PC that can send a site name nowhere, which makes that one site fail while everything else works. Ad blockers, some game mods and cheats, and malware all add entries. To review it, open Notepad as administrator (right-click Notepad > Run as administrator), choose File > Open, set the file type to All files, and open C:\\Windows\\System32\\drivers\\etc\\hosts. A line starting with # is a comment and does nothing. Delete or put a # in front of an entry for the site that won't load, then save. Only remove entries you recognize and don't need: an ad blocker's list is deliberate. If you didn't add them and they point at big sites, have a technician check the PC for malware." },
                        new() { Label = "Checking for firewall rules that block connections", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows Firewall isn't blocking any outgoing connections.", FixLabel = "How to review the rules", RealCheck = WebAccessDiagnosticsService.CheckFirewallBlocking,
                            FixInfoText = "By default Windows Firewall only guards incoming connections, so a rule that blocks OUTGOING ones was added by someone or something on purpose. To review: press the Windows key, type \"Windows Defender Firewall with Advanced Security\", open Outbound Rules, and look for rules with a red block symbol. If one covers the program or game that won't connect, right-click it and choose Disable Rule. Leave any rule your IT department set. If you use a security suite such as ESET, Norton or McAfee, its own firewall can block sites too: check its settings or its blocked-connections log." },
                        new() { Label = "Checking your browsers' saved data", ScriptedStatus = CheckStatus.Pass, ResultText = "Your browsers' caches are a normal size.", FixLabel = "Clear browser caches", RealCheck = WebAccessDiagnosticsService.CheckBrowserCache, Tier = FixTier.SafeAutoFix, ApplyFix = WebAccessDiagnosticsService.ClearBrowserCachesAsync },
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
        // Reached from its own card at the bottom of the home screen, not one of the four area tiles.
        // Two options: Windows' own antivirus (a real engine, the trustworthy verdict) and Vitals' own
        // look for suspicious-looking files (finds things worth a second look, never claims a verdict).
        new Section
        {
            Key = SectionKey.Security,
            Label = "Scan suspicious files",
            Description = "Look for malware and risky files",
            Headline = "How would you like to scan?",
            AccentBrushKey = "Vitals.Purple.Brush",
            AccentDimBrushKey = "Vitals.Purple.Brush",
            Symptoms = WithRunBoth(new List<Symptom>
            {
                new Symptom
                {
                    Id = "defender", Title = "Scan with Microsoft Defender",
                    Subtitle = "Use Windows' built-in antivirus to scan the places malware usually hides.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking Microsoft Defender is on", ScriptedStatus = CheckStatus.Pass, ResultText = "Microsoft Defender is on and watching in real time.", FixLabel = "See how to turn it on", RealCheck = SecurityScanService.CheckDefenderStatus,
                            FixInfoText = "Open Windows Security (press the Windows key, type \"Windows Security\"), choose Virus & threat protection, and turn on Real-time protection. If you use another antivirus (Norton, McAfee, Bitdefender and so on), Defender stays off or in standby on purpose - that's normal, and you should run that program's scan instead. If Defender won't turn on and you don't have another antivirus, a technician should look, since malware sometimes switches it off." },
                        new() { Label = "Checking virus definitions are current", ScriptedStatus = CheckStatus.Pass, ResultText = "Defender's virus definitions were updated today.", FixLabel = "Update virus definitions", RealCheck = SecurityScanService.CheckDefenderDefinitions, Tier = FixTier.SafeAutoFix, ApplyFix = SecurityScanService.UpdateDefenderDefinitionsAsync },
                        new() { Label = "Running a Defender quick scan (a few minutes)", ScriptedStatus = CheckStatus.Pass, ResultText = "Defender's quick scan finished and found nothing.", FixLabel = "See what to do next", RealCheck = SecurityScanService.CheckDefenderQuickScan,
                            FixInfoText = "If you use another antivirus (ESET, Norton, McAfee, Bitdefender and so on), Windows keeps Defender off on purpose and Vitals can't start that program's scan - open it and run a scan from there. If Defender did find something, it has already dealt with what it was sure about - it quarantines or removes threats automatically, and Vitals never deletes anything itself. To see exactly what it did, open Windows Security > Virus & threat protection > Protection history, and approve any item still waiting for you. If the same threat keeps coming back, or you can't tell what it was, have a technician look. If it might have been a password stealer, change your important passwords from a different, clean device." },
                    }
                },
                new Symptom
                {
                    Id = "vitalsfiles", Title = "Check for suspicious-looking files",
                    Subtitle = "Vitals looks through Downloads, Desktop, Temp and startup for disguised or unsigned programs.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Looking for programs disguised as documents", ScriptedStatus = CheckStatus.Pass, ResultText = "No programs pretending to be documents or pictures.", FixLabel = "See what to do", RealCheck = SuspiciousFileService.CheckDisguisedFiles,
                            FixInfoText = "A file named like invoice.pdf.exe is a program dressed up as a document - Windows hides the real ending by default, so it looks harmless. Don't open it. If you don't know where it came from, the safest move is to delete it, then run the Microsoft Defender scan. If you did open one, disconnect from the internet and have a technician check the PC. To see real file endings yourself: in File Explorer choose View > Show > File name extensions." },
                        new() { Label = "Checking recent downloads for unsigned programs", ScriptedStatus = CheckStatus.Pass, ResultText = "Every recent program has a valid digital signature.", FixLabel = "What does unsigned mean?", RealCheck = SuspiciousFileService.CheckUnsignedPrograms,
                            FixInfoText = "A digital signature proves who made a program and that it hasn't been changed since. Unsigned doesn't mean unsafe - hobby tools, older programs and many game mods aren't signed - but malware is almost never signed by a trusted company. For each one listed, ask: do I know where I got this? If not, don't run it. For a closer look, a technician can paste its SHA-256 (shown in Tech Mode) into a reputation site such as virustotal.com, which compares it against dozens of antivirus engines without uploading the file." },
                        new() { Label = "Checking what starts with Windows", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing starts from an unusual folder.", FixLabel = "See what to do", RealCheck = SuspiciousFileService.CheckStartupEntries,
                            FixInfoText = "Real software installs into Program Files and starts from there. Something set to launch with Windows from a Temp or Downloads folder is a common malware pattern: it dropped itself there and registered to survive a restart. Open Task Manager (Ctrl+Shift+Esc), go to the Startup apps tab, and turn off anything you don't recognise. Then run the Microsoft Defender scan. If an entry keeps coming back, a technician should look." },
                    }
                },
            })
        },
    };

    /// <summary>
    /// Appends a "run both scans" option made from the first two symptoms' own checks, so it can never
    /// drift out of sync with them. Defender goes first because its verdict matters most; Vitals' own
    /// check then covers the suspicious-looking files Defender may not flag.
    /// </summary>
    private static List<Symptom> WithRunBoth(List<Symptom> scans)
    {
        scans.Add(new Symptom
        {
            Id = "scanboth", Title = "Run both scans",
            Subtitle = "Microsoft Defender first, then Vitals' own check, one after the other.",
            Checks = scans.SelectMany(s => s.Checks).ToList(),
        });
        return scans;
    }

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
