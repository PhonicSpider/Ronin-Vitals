using System.IO;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// The sections, symptom cards and checks the app shows. Every check runs a real diagnostic; the scripted status and
/// text on each one are only the placeholder shown before it runs.
/// </summary>
public static class VitalsCatalog
{
    private const string StartupGuidance =
        "Press Ctrl+Shift+Esc to open Task Manager, go to the Startup apps tab and sort by Startup impact. Right-click anything you don't need running all the time (chat apps, game launchers, updaters, printer software) and choose Disable. This doesn't uninstall anything; you can turn it back on the same way. Leave anything from your antivirus, your graphics driver, and your touchpad or keyboard maker.";

    private const string RestartGuidance =
        "Save your work, then choose Start > Power > Restart (not Shut down). Windows 10 and 11 keep part of the system running across a normal shutdown (Fast startup), so only Restart gives a fully fresh start and lets waiting updates finish. If a restart doesn't clear the problem, the other checks on this card point to the next step.";

    private const string WindowsUpdateGuidance =
        "Open Settings > Windows Update and press Check for updates, install what it offers, and restart when asked. Keeping Windows current fixes many crashes and update errors. If the check fails or keeps failing, the \"Windows won't update\" card in the System area digs into why.";

    private const string RepairWindowsGuidance =
        "Open Terminal as administrator (right-click the Start button > Terminal (Admin)). Run DISM /Online /Cleanup-Image /RestoreHealth and wait for it to finish (it can take 15 minutes and needs the internet), then run sfc /scannow, then restart. If either says it can't repair, a repair install keeps your files and apps: Settings > System > Recovery > Fix problems using Windows Update > Reinstall now.";

    private const string ThrottlingGuidance =
        "A CPU slows itself down when it gets too hot or hits a power limit, and everything feels slower. Check the temperature and fan items on the \"Fans are loud or it's overheating\" card, clean dust from the fans and vents, and make sure the PC has air around it. On a laptop, use it on a hard surface, not a bed or sofa. If the PC is a desktop and it still happens, the CPU cooler may need re-seating or fresh thermal paste, which a technician can do.";

    private const string CoolingGuidance =
        "Switch the PC off and unplug it, then blow dust out of the fans, filters and vents with compressed air, holding the fan blades still. Keep vents clear of walls and cushions, and keep a desktop where air can reach it. On a laptop, use a hard flat surface or a cooling stand. If it still runs hot after a clean, the thermal paste on the CPU may have dried out, which a technician can replace. Note that Windows can only see a motherboard sensor, so for the exact CPU temperature a tool such as HWiNFO reads it directly.";

    private const string BackupGuidance =
        "Copy what matters (documents, photos, saves, anything you can't replace) to a different drive or a cloud service now, before doing anything else. In Settings > Accounts > Windows backup you can turn on OneDrive folder backup, or just drag your important folders to an external drive. Once it's safe, ask a technician to check the drive, and expect to replace it if the warnings continue.";

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
                        new() { Label = "Checking CPU load", ScriptedStatus = CheckStatus.Pass, ResultText = "CPU usage is normal.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckCpuLoad,
                            FixInfoText = "A busy CPU means something is working hard right now. Press Ctrl+Shift+Esc to open Task Manager, click the CPU column to sort, and see what's at the top. If it's a program you're using, that's normal. If it's something you don't recognise, or the CPU stays high while you're idle, close that program, and if it keeps coming back run the Scan suspicious files option on the Home screen. The \"Something's using too much CPU or RAM\" card digs further." },
                        new() { Label = "Checking memory usage", ScriptedStatus = CheckStatus.Warn, ResultText = "RAM usage is high - several programs are competing for memory.", FixLabel = "Show what's using RAM", RealCheck = SystemDiagnosticsService.CheckMemoryUsage,
                            FixInfoText = "When memory runs short, Windows starts using the much slower drive instead, and everything feels sluggish. Press Ctrl+Shift+Esc, open Task Manager, and click the Memory column to see the biggest users. Browsers with many tabs are the usual cause: close tabs you aren't using. Restarting the PC also clears memory that programs failed to give back. If your memory is high even with few programs open, adding more RAM is the lasting fix." },
                        new() { Label = "Checking disk activity", ScriptedStatus = CheckStatus.Pass, ResultText = "Disk activity is normal.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckDiskActivity,
                            FixInfoText = "A disk running at full tilt makes the whole PC feel stuck, especially on a hard drive. Open Task Manager (Ctrl+Shift+Esc) and click the Disk column to see what's reading or writing. Windows Update, an antivirus scan or a game updating are normal and pass on their own. If it stays pinned for a long time, check the drive health card, since a failing drive often shows up this way." },
                        new() { Label = "Checking startup programs", ScriptedStatus = CheckStatus.Bad, ResultText = "12 programs are launching automatically at startup.", FixLabel = "Manage startup programs", RealCheck = SystemDiagnosticsService.CheckStartupPrograms, FixInfoText = StartupGuidance },
                        new() { Label = "Checking for file fragmentation", ScriptedStatus = CheckStatus.Pass, ResultText = "No significant fragmentation found.", FixLabel = "How to defragment", RealCheck = DriveFragmentationService.CheckFragmentation,
                            FixInfoText = "Only hard drives (not SSDs) slow down from fragmentation. To fix it: press the Windows key, type \"Defragment and Optimize Drives\", pick the hard drive, and press Optimize. It can take a while, and the PC stays usable. Don't run it on an SSD; Windows already looks after those." },
                        new() { Label = "Checking free disk space", ScriptedStatus = CheckStatus.Pass, ResultText = "Plenty of free space available.", FixLabel = "Clean temporary files", RealCheck = SystemDiagnosticsService.CheckFreeDiskSpace, Tier = FixTier.SafeAutoFix, ApplyFix = SystemDiagnosticsService.CleanTempFilesAsync },
                        new() { Label = "Checking your power plan", ScriptedStatus = CheckStatus.Pass, ResultText = "Your power plan doesn't hold back performance.", FixLabel = "How to change it", RealCheck = ThermalDiagnosticsService.CheckPowerPlanForSpeed,
                            FixInfoText = "Power saver slows the PC down on purpose. Open Settings > System > Power & battery and set Power mode to Balanced or Best performance. On older Windows, open Control Panel > Power Options and choose Balanced. A laptop on battery will last a little less, which is the trade." },
                        new() { Label = "Checking whether the CPU is being slowed down", ScriptedStatus = CheckStatus.Pass, ResultText = "The CPU isn't being slowed down.", FixLabel = "See what to do", RealCheck = ThermalDiagnosticsService.CheckThermalThrottling, FixInfoText = ThrottlingGuidance },
                        new() { Label = "Checking how long since a full restart", ScriptedStatus = CheckStatus.Pass, ResultText = "The PC was restarted recently.", FixLabel = "How to restart properly", RealCheck = SystemDiagnosticsService.CheckTimeSinceRestart, FixInfoText = RestartGuidance },
                    }
                },
                new Symptom
                {
                    Id = "hot", Title = "Fans are loud or it's overheating",
                    Subtitle = "Check temperatures and airflow-related issues.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Reading temperature", ScriptedStatus = CheckStatus.Warn, ResultText = "CPU is running warmer than ideal under load.", FixLabel = "Show cooling tips", RealCheck = ThermalDiagnosticsService.CheckTemperature, FixInfoText = CoolingGuidance },
                        new() { Label = "Checking for thermal throttling", ScriptedStatus = CheckStatus.Pass, ResultText = "No throttling detected right now.", FixLabel = "See what to do", RealCheck = ThermalDiagnosticsService.CheckThermalThrottling, FixInfoText = ThrottlingGuidance },
                        new() { Label = "Checking the fans Windows can see", ScriptedStatus = CheckStatus.Pass, ResultText = "Fans are responding normally.", FixLabel = "How to check your fans", RealCheck = ThermalDiagnosticsService.CheckFans,
                            FixInfoText = "Windows can't measure fan speed on most PCs, so this relies on your ears and a look inside. Loud, grinding or rattling means a worn fan or one hitting a cable. A fan that's silent when the PC is hot means it has stopped. On a desktop, switch the PC off and unplug it, open the side panel and blow dust from the fans, filters and heatsink with compressed air, holding the fan blades still so they don't over-spin. On a laptop, blow air through the vents; for a deeper clean, a technician can open it. Fan curves can also be changed in the BIOS or with the maker's own fan software." },
                        new() { Label = "Checking how hard the CPU is working", ScriptedStatus = CheckStatus.Pass, ResultText = "CPU usage is normal.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckCpuLoad,
                            FixInfoText = "A CPU that works hard makes heat, and heat makes fans loud. Open Task Manager (Ctrl+Shift+Esc), sort by CPU, and close what you don't need. Games and video editing legitimately run the fans hard. A PC that's loud while doing nothing has a program stuck in the background, so check the next item." },
                        new() { Label = "Checking what is keeping the CPU busy", ScriptedStatus = CheckStatus.Warn, ResultText = "One background app is using significant CPU.", FixLabel = "Show top processes", RealCheck = SystemDiagnosticsService.CheckTopCpuConsumer,
                            FixInfoText = "Open Task Manager (Ctrl+Shift+Esc) and sort the Processes tab by CPU. If the busy program is one you don't need, right-click it and choose End task. If it's a Windows process such as Antimalware Service Executable or Windows Update, it usually settles after a scan or update finishes. If it never settles, or you don't recognise the name, run the Scan suspicious files option on the Home screen." },
                        new() { Label = "Checking your power plan", ScriptedStatus = CheckStatus.Pass, ResultText = "Your power plan lets the CPU cool down when it's idle.", FixLabel = "How to change it", RealCheck = ThermalDiagnosticsService.CheckPowerPlanForHeat,
                            FixInfoText = "High performance and Ultimate performance keep the CPU running fast even when you're not busy, which costs heat and fan noise. Open Settings > System > Power & battery and set Power mode to Balanced. The PC stays just as quick when you need it, because Balanced speeds up on demand." },
                    }
                },
                new Symptom
                {
                    Id = "drivefail", Title = "A drive might be failing",
                    Subtitle = "Check drive health before you lose anything.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Reading drive health status", ScriptedStatus = CheckStatus.Bad, ResultText = "Your drive is reporting early signs of wear.", FixLabel = "Back up this drive now", RealCheck = DriveDiagnosticsService.CheckDriveHealth, FixInfoText = BackupGuidance },
                        new() { Label = "Checking drive wear and temperature", ScriptedStatus = CheckStatus.Pass, ResultText = "Your drives report normal wear and temperature.", FixLabel = "See what to do", RealCheck = DriveDiagnosticsService.CheckDriveWear,
                            FixInfoText = "SSDs wear out by amount written, and a drive reporting most of its life used should be replaced soon. A hot drive (over about 60 degrees C) ages faster: check airflow around it, and for an M.2 drive consider a heatsink. In both cases, back up what matters first. Hard drives usually don't report wear at all, so for those the health and error checks matter more." },
                        new() { Label = "Checking free space", ScriptedStatus = CheckStatus.Pass, ResultText = "Plenty of free space available.", FixLabel = "Clean temporary files", RealCheck = SystemDiagnosticsService.CheckFreeDiskSpace, Tier = FixTier.SafeAutoFix, ApplyFix = SystemDiagnosticsService.CleanTempFilesAsync },
                        new() { Label = "Checking for read/write errors", ScriptedStatus = CheckStatus.Warn, ResultText = "A few recoverable errors were found.", FixLabel = "Show error details", RealCheck = EventLogDiagnosticsService.CheckDiskErrors,
                            FixInfoText = "Windows logs a disk error every time a read or write failed. A single one can be a loose cable or a bad shutdown. Repeated ones mean trouble. Open Event Viewer (press the Windows key, type Event Viewer) > Windows Logs > System, and look for entries from \"disk\" or \"Ntfs\" to see which drive. Back up first, then check the drive's cable and run a disk check: open File Explorer, right-click the drive > Properties > Tools > Check." },
                        new() { Label = "Checking whether Windows wants to scan a drive", ScriptedStatus = CheckStatus.Pass, ResultText = "No drive is flagged as needing a disk check.", FixLabel = "How to scan a drive", RealCheck = BootDiagnosticsService.CheckDiskFlags,
                            FixInfoText = "Windows sets this flag when a drive wasn't shut down cleanly or when it found errors it couldn't fix on its own. Back up what matters first. Then open File Explorer, right-click the flagged drive > Properties > Tools > Check > Scan drive. For a full repair, open Terminal as administrator and run chkdsk with the drive letter and /f; Windows may ask to run it at the next restart, which is normal." },
                    }
                },
                new Symptom
                {
                    Id = "hwall", Title = "Not sure — check everything", IsCatchAll = true,
                    Subtitle = "A general hardware health check, top to bottom.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking CPU health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckCpuLoad,
                            FixInfoText = "A busy CPU means something is working hard right now. Open Task Manager (Ctrl+Shift+Esc), sort by CPU, and close what you don't need. The \"My computer feels slow\" card goes deeper." },
                        new() { Label = "Checking memory health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckMemoryUsage,
                            FixInfoText = "Open Task Manager (Ctrl+Shift+Esc), click the Memory column, and close the biggest programs you don't need, browser tabs first. If memory is high even with little open, adding RAM is the lasting fix." },
                        new() { Label = "Checking drive health", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", FixLabel = "Back up this drive now", RealCheck = DriveDiagnosticsService.CheckDriveHealth, FixInfoText = BackupGuidance },
                        new() { Label = "Checking temperatures", ScriptedStatus = CheckStatus.Pass, ResultText = "All within normal range.", FixLabel = "Show cooling tips", RealCheck = ThermalDiagnosticsService.CheckTemperature, FixInfoText = CoolingGuidance },
                        new() { Label = "Checking whether the CPU is being slowed down", ScriptedStatus = CheckStatus.Pass, ResultText = "The CPU isn't being slowed down.", FixLabel = "See what to do", RealCheck = ThermalDiagnosticsService.CheckThermalThrottling, FixInfoText = ThrottlingGuidance },
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
                        new() { Label = "Reading recent crash logs", ScriptedStatus = CheckStatus.Bad, ResultText = "This program has crashed 4 times in the last day.", FixLabel = "Show crash details", RealCheck = EventLogDiagnosticsService.CheckRecentCrashes,
                            FixInfoText = "Start with the program named above. Update it, then if it still crashes uninstall it (Settings > Apps) and install a fresh copy from the maker's site. Games and creative tools often need a newer graphics driver: if the result mentions a graphics driver, update it from NVIDIA, AMD or Intel directly. For the full detail, press the Windows key, type \"Reliability history\" and open it: it lists every crash with a date. If several different programs crash, suspect memory or Windows itself and look at the other checks here." },
                        new() { Label = "Checking Windows' own files for damage", ScriptedStatus = CheckStatus.Pass, ResultText = "No corrupted files found.", FixLabel = "How to repair Windows", RealCheck = SystemIntegrityService.CheckComponentStore, FixInfoText = RepairWindowsGuidance },
                        new() { Label = "Checking for waiting Windows updates", ScriptedStatus = CheckStatus.Warn, ResultText = "An update is available that may fix this.", FixLabel = "How to install updates", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingUpdates, FixInfoText = WindowsUpdateGuidance },
                        new() { Label = "Checking memory usage", ScriptedStatus = CheckStatus.Pass, ResultText = "RAM usage is normal.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckMemoryUsage,
                            FixInfoText = "A program that runs out of memory crashes. Close other programs and browser tabs before running the one that crashes. If memory is high with little open, or many different programs crash, a memory fault is possible: press the Windows key, type \"Windows Memory Diagnostic\", and choose Restart now and check for problems." },
                    }
                },
                new Symptom
                {
                    Id = "boot", Title = "Computer won't start up properly",
                    Subtitle = "Check startup, updates, and disk issues.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking how long Windows takes to start", ScriptedStatus = CheckStatus.Warn, ResultText = "Startup is slower than usual.", FixLabel = "Manage startup programs", RealCheck = BootDiagnosticsService.CheckStartupTime, FixInfoText = StartupGuidance },
                        new() { Label = "Checking for unexpected shutdowns and blue screens", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows has shut down cleanly every time.", FixLabel = "See what to do", RealCheck = BootDiagnosticsService.CheckUnexpectedShutdowns,
                            FixInfoText = "An unexpected shutdown is a power cut, a hard reset, or the PC freezing or crashing. A blue screen shows a stop code: search it online with the maker's name for the cause. Common ones point at a graphics driver, a failing drive, bad memory, or an overheating CPU. Update your graphics driver, check the drive and temperature cards, and run Windows Memory Diagnostic (press the Windows key and type it). If it happens when the PC is idle or under load only, power supply and heat are the usual suspects." },
                        new() { Label = "Checking whether Windows is waiting for a restart", ScriptedStatus = CheckStatus.Bad, ResultText = "An update is stuck and needs attention.", FixLabel = "How to restart properly", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingRestart, FixInfoText = RestartGuidance },
                        new() { Label = "Checking Windows' recovery tools", ScriptedStatus = CheckStatus.Pass, ResultText = "Recovery tools are ready.", FixLabel = "How to turn them on", RealCheck = BootDiagnosticsService.CheckRecoveryEnvironment,
                            FixInfoText = "Windows' recovery environment is what offers Startup Repair, System Restore and Reset this PC when Windows can't start. To turn it on, open Terminal as administrator and run: reagentc /enable. If that reports it can't find the recovery image, a technician or a Windows repair install can rebuild it." },
                        new() { Label = "Checking whether Windows wants to scan a drive", ScriptedStatus = CheckStatus.Pass, ResultText = "No drive is flagged as needing a disk check.", FixLabel = "How to scan a drive", RealCheck = BootDiagnosticsService.CheckDiskFlags,
                            FixInfoText = "Windows sets this flag when a drive wasn't shut down cleanly or when it found errors it couldn't fix on its own. Open File Explorer, right-click the flagged drive > Properties > Tools > Check > Scan drive. For a full repair, open Terminal as administrator and run chkdsk with the drive letter and /f; Windows may ask to run it at the next restart, which is normal." },
                        new() { Label = "Checking startup programs", ScriptedStatus = CheckStatus.Warn, ResultText = "Startup is slower than usual.", FixLabel = "Manage startup programs", RealCheck = SystemDiagnosticsService.CheckStartupPrograms, FixInfoText = StartupGuidance },
                    }
                },
                new Symptom
                {
                    Id = "resource", Title = "Something's using too much CPU or RAM",
                    Subtitle = "Find out what's eating your resources.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking top CPU consumers", ScriptedStatus = CheckStatus.Warn, ResultText = "One background app is using significant CPU.", FixLabel = "Show top processes", RealCheck = SystemDiagnosticsService.CheckTopCpuConsumer,
                            FixInfoText = "Open Task Manager (Ctrl+Shift+Esc) and sort the Processes tab by CPU. If the busy program is one you don't need, right-click it and choose End task. If it's a Windows process such as Antimalware Service Executable or Windows Update, it usually settles after a scan or update finishes. If it never settles, or you don't recognise the name, run the Scan suspicious files option on the Home screen." },
                        new() { Label = "Checking top memory consumers", ScriptedStatus = CheckStatus.Pass, ResultText = "No single program is hogging memory.", FixLabel = "Show top processes", RealCheck = ProcessDiagnosticsService.CheckTopMemoryConsumer,
                            FixInfoText = "Open Task Manager (Ctrl+Shift+Esc) and click the Memory column. A browser is often the biggest user because every tab and extension counts: close tabs, and disable extensions you don't use. A program whose memory keeps climbing the longer it runs has a memory leak: restarting it frees the memory, and updating it may fix the leak for good." },
                        new() { Label = "Checking for programs that stopped responding", ScriptedStatus = CheckStatus.Pass, ResultText = "Every open program is responding.", FixLabel = "How to close a frozen program", RealCheck = ProcessDiagnosticsService.CheckNotResponding,
                            FixInfoText = "A program marked Not responding is stuck and often holding CPU or memory. Wait a minute in case it's busy, then press Ctrl+Shift+Esc, find it in Task Manager, right-click and choose End task. You may lose unsaved work in that program. If the same program freezes repeatedly, see the \"A program keeps crashing\" card." },
                        new() { Label = "Checking overall memory usage", ScriptedStatus = CheckStatus.Pass, ResultText = "RAM usage is normal.", FixLabel = "See what to do", RealCheck = SystemDiagnosticsService.CheckMemoryUsage,
                            FixInfoText = "Close programs and browser tabs you aren't using, and restart the PC if it has been on for days. If memory is high even with little open, adding RAM is the lasting fix." },
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
                        new() { Label = "Checking the Windows Update services", ScriptedStatus = CheckStatus.Pass, ResultText = "The update service is running.", FixLabel = "How to switch them back on", RealCheck = WindowsUpdateDiagnosticsService.CheckUpdateServices,
                            FixInfoText = "Windows Update needs four services to be allowed to run. If one is switched off, press Win+R, type services.msc and press Enter, double-click the named service, set Startup type to Manual (or Automatic for Background Intelligent Transfer and Cryptographic Services), and press Apply. They start themselves when needed, so seeing them \"Stopped\" is normal. Some \"PC optimizer\" tools switch these off, which is worth knowing if you've used one." },
                        new() { Label = "Checking room on the Windows drive", ScriptedStatus = CheckStatus.Bad, ResultText = "Not enough free space to complete the update.", FixLabel = "Free up disk space", RealCheck = WindowsUpdateDiagnosticsService.CheckSystemDriveSpace, Tier = FixTier.SafeAutoFix, ApplyFix = SystemDiagnosticsService.CleanTempFilesAsync },
                        new() { Label = "Checking for stuck update files", ScriptedStatus = CheckStatus.Warn, ResultText = "Some leftover update files may be causing issues.", FixLabel = "How to clear the update cache", RealCheck = SystemDiagnosticsService.CheckStuckUpdateFiles,
                            FixInfoText = "A damaged download blocks every later update until it's cleared. Open Terminal as administrator (right-click Start > Terminal (Admin)) and run these four lines one at a time: net stop wuauserv, net stop bits, then rd /s /q C:\\Windows\\SoftwareDistribution\\Download, then net start bits, and finally net start wuauserv. Windows downloads what it needs again, so nothing is lost. Then open Settings > Windows Update and check for updates." },
                        new() { Label = "Checking whether Windows is waiting for a restart", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing is waiting for a restart.", FixLabel = "How to restart properly", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingRestart, FixInfoText = RestartGuidance },
                        new() { Label = "Checking for failed updates", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows hasn't logged any failed updates.", FixLabel = "See what to do", RealCheck = WindowsUpdateDiagnosticsService.CheckUpdateFailures,
                            FixInfoText = "The error code in the result is Windows' own reason, and searching it online finds fixes for that exact code. The common ones: a damaged download (clear the update cache, see the card above), not enough room (free up space on the Windows drive), damaged system files (see the \"Settings keep resetting\" card for the repair), or a network or VPN problem reaching Microsoft (turn off any VPN and try again). Settings > Windows Update > Update history shows the same failures by name." },
                        new() { Label = "Checking for waiting Windows updates", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows is up to date.", FixLabel = "How to install updates", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingUpdates, FixInfoText = WindowsUpdateGuidance },
                    }
                },
                new Symptom
                {
                    Id = "settings", Title = "Settings keep resetting or acting weird",
                    Subtitle = "Check your system files and user profile.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Verifying system files (takes several minutes)", ScriptedStatus = CheckStatus.Pass, ResultText = "All core system files check out.", FixLabel = "How to repair Windows", RealCheck = SystemIntegrityService.CheckSystemFiles, FixInfoText = RepairWindowsGuidance },
                        new() { Label = "Checking Windows' repair store", ScriptedStatus = CheckStatus.Pass, ResultText = "No component store corruption recorded.", FixLabel = "How to repair Windows", RealCheck = SystemIntegrityService.CheckComponentStore, FixInfoText = RepairWindowsGuidance },
                        new() { Label = "Checking user profile health", ScriptedStatus = CheckStatus.Warn, ResultText = "Your profile shows signs of minor corruption.", FixLabel = "Show repair options", RealCheck = SystemIntegrityService.CheckUserProfile,
                            FixInfoText = "A profile that fails to load makes Windows sign you in to a temporary one, so every change vanishes at sign-out. That is exactly what \"settings keep resetting\" looks like. The reliable fix is to make a new user: Settings > Accounts > Other users > Add account, sign in to it, and copy your files across from C:\\Users\\<old name>. Back up the old profile folder first. Restarting once and signing in again sometimes clears a one-off failure, so try that before anything else." },
                        new() { Label = "Checking for settings locked by policy", ScriptedStatus = CheckStatus.Pass, ResultText = "No policies are locking any Windows settings.", FixLabel = "What this means", RealCheck = SystemIntegrityService.CheckPolicyControlledSettings,
                            FixInfoText = "A policy is an instruction that overrides a setting, and the Settings app usually shows \"Some settings are managed by your organization\" for it. Work PCs use them on purpose. On a home PC they usually come from a tweak, debloat or privacy tool, a game or app installer, or an older Windows edition setting. If you ran one of those tools, it has a way to undo its changes. If you want to see the entries yourself, press Win+R, type regedit and look under HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft; back up the key first, and leave anything you don't recognise to a technician." },
                    }
                },
                new Symptom
                {
                    Id = "general", Title = "General health check", IsCatchAll = true,
                    Subtitle = "A broad look at your system's overall condition.",
                    Checks = new List<CheckDefinition>
                    {
                        new() { Label = "Checking system files", ScriptedStatus = CheckStatus.Pass, ResultText = "Looks good.", FixLabel = "How to repair Windows", RealCheck = SystemIntegrityService.CheckComponentStore, FixInfoText = RepairWindowsGuidance },
                        new() { Label = "Checking the update services", ScriptedStatus = CheckStatus.Pass, ResultText = "Up to date.", FixLabel = "How to switch them back on", RealCheck = WindowsUpdateDiagnosticsService.CheckUpdateServices,
                            FixInfoText = "Windows Update needs four services to be allowed to run. If one is switched off, press Win+R, type services.msc and press Enter, double-click the named service, set Startup type to Manual (or Automatic for Background Intelligent Transfer and Cryptographic Services), and press Apply. Seeing them \"Stopped\" is normal; they start when needed." },
                        new() { Label = "Checking for waiting Windows updates", ScriptedStatus = CheckStatus.Pass, ResultText = "Windows is up to date.", FixLabel = "How to install updates", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingUpdates, FixInfoText = WindowsUpdateGuidance },
                        new() { Label = "Checking whether Windows is waiting for a restart", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing is waiting for a restart.", FixLabel = "How to restart properly", RealCheck = WindowsUpdateDiagnosticsService.CheckPendingRestart, FixInfoText = RestartGuidance },
                        new() { Label = "Checking recent errors", ScriptedStatus = CheckStatus.Pass, ResultText = "Nothing concerning found.", FixLabel = "How to read them", RealCheck = EventLogDiagnosticsService.CheckRecentSystemErrors,
                            FixInfoText = "Some errors are logged on every healthy PC, so a few mean little. To see them, press the Windows key, type Event Viewer, open Windows Logs > System, and click Filter Current Log > Error. Look at the Source column for names that repeat, and search the Event ID with the source name to find what it means. A pile of errors from \"disk\", \"Ntfs\" or \"volmgr\" means look at the drive cards; many from a driver or service name means that driver or service is the one to update or reinstall." },
                        new() { Label = "Checking room on the Windows drive", ScriptedStatus = CheckStatus.Pass, ResultText = "Plenty of room.", FixLabel = "Free up disk space", RealCheck = WindowsUpdateDiagnosticsService.CheckSystemDriveSpace, Tier = FixTier.SafeAutoFix, ApplyFix = SystemDiagnosticsService.CleanTempFilesAsync },
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
            Checks = scans.SelectMany(scan => scan.Checks).ToList(),
        });
        return scans;
    }

    public static Section GetSection(SectionKey key) => Sections.First(section => section.Key == key);

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
