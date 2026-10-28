using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Drives Microsoft Defender, the antivirus built into Windows, rather than pretending Vitals can be one.
/// Defender is a real, constantly updated engine; a homemade scanner would give people false confidence.
/// Status and definition age are read-only. The quick scan runs Defender's own scan, and Defender then
/// handles anything it is sure about by its normal policy (quarantine or remove), exactly as it would if
/// the user had started the scan in Windows Security. Vitals only reports the outcome and never deletes
/// or moves a file itself. The one fix, updating definitions, only downloads Microsoft's latest signatures.
/// If another antivirus is the active one, Defender may be off or passive, and the checks say so plainly.
/// </summary>
public static class SecurityScanService
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);

    private sealed record DefenderStatus(bool ServiceEnabled, bool AntivirusEnabled, bool RealTimeEnabled, string RunningMode, int SignatureAgeDays);

    private const string StatusCommand =
        "Get-MpComputerStatus | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AMRunningMode,AntivirusSignatureAge | ConvertTo-Json -Compress";

    private static DefenderStatus? ReadStatus(out string detail)
    {
        var result = PowerShellRunner.Run(StatusCommand, TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0 || result.Output.Length == 0)
        {
            detail = result.TimedOut ? "Timed out reading Defender status." : $"Get-MpComputerStatus failed: {FirstLine(result.Error)}";
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            var root = doc.RootElement;
            bool Flag(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
            var mode = root.TryGetProperty("AMRunningMode", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "Unknown" : "Unknown";
            var age = root.TryGetProperty("AntivirusSignatureAge", out var a) && a.TryGetInt32(out var days) ? days : -1;
            detail = result.Output;
            return new DefenderStatus(Flag("AMServiceEnabled"), Flag("AntivirusEnabled"), Flag("RealTimeProtectionEnabled"), mode, age);
        }
        catch (JsonException ex)
        {
            detail = $"Couldn't read Defender's answer: {ex.Message}";
            return null;
        }
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    /// <summary>
    /// Non-Defender antivirus programs that Windows Security Center says are switched on (ESET, Norton,
    /// McAfee, Bitdefender and so on). When one is active Windows deliberately turns Defender off, so
    /// "Defender is off" is NOT a problem there - it just means Vitals cannot run that program's scan.
    /// Decoded from productState: bit 0x1000 = "on". Found live: this PC runs ESET, and the first version
    /// of this check told its owner nothing was watching for threats.
    /// </summary>
    private static List<string> ReadOtherActiveAntivirus(out string detail)
    {
        var names = new List<string>();
        var seen = new List<string>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            foreach (System.Management.ManagementObject product in searcher.Get())
            {
                var name = product["displayName"] as string ?? "Unknown antivirus";
                var state = Convert.ToInt32(product["productState"] ?? 0);
                var on = (state & 0xF000) == 0x1000;
                seen.Add($"{name} (state 0x{state:X6}, {(on ? "on" : "off")})");
                if (on && !name.Contains("Defender", StringComparison.OrdinalIgnoreCase) && !names.Contains(name)) names.Add(name);
            }
            detail = seen.Count == 0 ? "Security Center lists no antivirus products." : "Security Center: " + string.Join("; ", seen.Distinct());
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidCastException)
        {
            detail = $"Couldn't read Security Center: {ex.Message}";
        }
        return names;
    }

    private static string Join(List<string> names) => names.Count == 1 ? names[0] : string.Join(" and ", names);

    private static bool DefenderIsRunning(DefenderStatus s) => s.ServiceEnabled && s.AntivirusEnabled
        && !s.RunningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase) && !s.RunningMode.Contains("EDR", StringComparison.OrdinalIgnoreCase);

    private const string StatusTechCommand = "Get-MpComputerStatus | Select-Object AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, AMRunningMode, AntivirusSignatureAge";

    // ── Is Defender on? ─────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckDefenderStatus()
    {
        var status = ReadStatus(out var detail);
        if (status is null)
            return new CheckResult(CheckStatus.Warn, "Microsoft Defender isn't available here - another antivirus may be in charge of this PC. Scan with that program instead.", StatusTechCommand, detail);

        if (!DefenderIsRunning(status))
        {
            var others = ReadOtherActiveAntivirus(out var avDetail);
            var combined = $"{detail} | {avDetail}";
            if (others.Count > 0)
                return new CheckResult(CheckStatus.Warn,
                    $"{Join(others)} is your antivirus, so Windows keeps Microsoft Defender switched off - that's normal. Vitals can only start Defender scans, so open {Join(others)} and run its scan from there.",
                    StatusTechCommand, combined);

            return status.RunningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase) || status.RunningMode.Contains("EDR", StringComparison.OrdinalIgnoreCase)
                ? new CheckResult(CheckStatus.Warn, "Another security product is in charge on this PC and Defender is standing by. Run that product's scan too.", StatusTechCommand, combined)
                : new CheckResult(CheckStatus.Bad, "Microsoft Defender is turned off and no other antivirus is protecting this PC, so nothing is watching for threats.", StatusTechCommand, combined);
        }

        if (!status.RealTimeEnabled)
            return new CheckResult(CheckStatus.Warn, "Defender's real-time protection is off, so threats aren't blocked as they appear. A scan can still find them.", StatusTechCommand, detail);

        return new CheckResult(CheckStatus.Pass, "Microsoft Defender is on and watching in real time.", StatusTechCommand, detail);
    }

    // ── Are its virus definitions current? ──────────────────────────────────────────────────────

    public static CheckResult CheckDefenderDefinitions()
    {
        var status = ReadStatus(out var detail);
        if (status is null || status.SignatureAgeDays < 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't tell how recent Defender's virus definitions are.", StatusTechCommand, detail);

        if (!DefenderIsRunning(status))
        {
            var others = ReadOtherActiveAntivirus(out var avDetail);
            return others.Count > 0
                ? new CheckResult(CheckStatus.Pass, $"Doesn't apply here - {Join(others)} is your antivirus and manages its own virus definitions.", StatusTechCommand, $"{detail} | {avDetail}")
                : new CheckResult(CheckStatus.Warn, "Defender isn't running, so its virus definitions aren't being kept up to date.", StatusTechCommand, $"{detail} | {avDetail}");
        }

        var age = status.SignatureAgeDays;
        if (age >= 65535) // Defender's own "never updated" marker, not a real number of days
            return new CheckResult(CheckStatus.Bad, "Defender has never downloaded virus definitions, so a scan has nothing to compare against.", StatusTechCommand, $"AntivirusSignatureAge = {age} (never updated). Raw: {detail}");

        var when = age == 0 ? "today" : age == 1 ? "yesterday" : $"{age} days ago";
        var techDetail = $"AntivirusSignatureAge = {age} day(s). Raw: {detail}";
        return age switch
        {
            <= 3 => new CheckResult(CheckStatus.Pass, $"Defender's virus definitions were updated {when}.", StatusTechCommand, techDetail),
            <= 7 => new CheckResult(CheckStatus.Warn, $"Defender's virus definitions were last updated {when} - a scan with old definitions can miss newer threats.", StatusTechCommand, techDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Defender's virus definitions are {age} days old - a scan with definitions this old can miss recent threats.", StatusTechCommand, techDetail),
        };
    }

    /// <summary>SafeAutoFix - asks Defender to download Microsoft's latest definitions (Update-MpSignature).</summary>
    public static async Task<bool> UpdateDefenderDefinitionsAsync() =>
        await Task.Run(() => PowerShellRunner.Run("Update-MpSignature", UpdateTimeout).ExitCode == 0);

    // ── The scan itself ─────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckDefenderQuickScan()
    {
        const string techCommand = "Start-MpScan -ScanType QuickScan; Get-MpThreatDetection | Where-Object InitialDetectionTime -ge <scan start>";

        var status = ReadStatus(out var statusDetail);
        if (status is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't start a Defender scan - Defender doesn't seem to be available here.", techCommand, statusDetail);

        if (!DefenderIsRunning(status))
        {
            var others = ReadOtherActiveAntivirus(out var avDetail);
            return others.Count > 0
                ? new CheckResult(CheckStatus.Warn, $"Vitals can't scan with Defender because {Join(others)} is your antivirus. Open {Join(others)} and run its scan from there.", techCommand, $"{statusDetail} | {avDetail}")
                : new CheckResult(CheckStatus.Warn, "Defender isn't running, so there's nothing for Vitals to scan with. Turn it on in Windows Security, then run this again.", techCommand, $"{statusDetail} | {avDetail}");
        }

        // Millisecond-free ISO text: PowerShell's [datetime] cast parses it the same way in every culture.
        var started = DateTime.Now;
        var clock = Stopwatch.StartNew();
        var scan = PowerShellRunner.Run("Start-MpScan -ScanType QuickScan", ScanTimeout);
        clock.Stop();

        if (scan.TimedOut)
            return new CheckResult(CheckStatus.Warn, "Defender's quick scan is taking much longer than usual, so Vitals stopped waiting. It may still be running - check Windows Security.", techCommand, $"No result after {ScanTimeout.TotalMinutes:0} minutes.");
        if (scan.ExitCode != 0)
            return new CheckResult(CheckStatus.Warn, "Defender couldn't run the scan from here. You can start one yourself in Windows Security > Virus & threat protection.", techCommand, $"Start-MpScan failed: {FirstLine(scan.Error)}");

        var duration = $"{(int)clock.Elapsed.TotalMinutes}m {clock.Elapsed.Seconds}s";
        var iso = started.AddSeconds(-2).ToString("yyyy-MM-ddTHH:mm:ss");
        var query = PowerShellRunner.Run(
            $"$s=[datetime]'{iso}'; $names=@{{}}; Get-MpThreat | ForEach-Object {{ $names[$_.ThreatID]=$_.ThreatName }}; " +
            "@(Get-MpThreatDetection | Where-Object { $_.InitialDetectionTime -ge $s } | ForEach-Object { [pscustomobject]@{ Name=$names[$_.ThreatID]; Resources=($_.Resources -join '; ') } }) | ConvertTo-Json -Compress",
            TimeSpan.FromSeconds(60));

        var found = ParseDetections(query.Output);
        if (query.ExitCode != 0)
            return new CheckResult(CheckStatus.Warn, "Defender finished its scan, but Vitals couldn't read the result. Check Windows Security > Protection history.", techCommand, $"Scan took {duration}. Result query failed: {FirstLine(query.Error)}");

        if (found.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"Defender's quick scan finished (it took {duration}) and found nothing.", techCommand, $"Scan took {duration}. No detections since {iso}.");

        var names = found.Select(f => f.Name).Distinct().ToList();
        var shown = string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" and {names.Count - 3} more" : "");
        return new CheckResult(CheckStatus.Bad,
            $"Defender's quick scan found {found.Count} {(found.Count == 1 ? "threat" : "threats")} ({shown}). It has already acted on what it was sure about.",
            techCommand,
            $"Scan took {duration}. Detections: " + string.Join(" | ", found.Select(f => $"{f.Name} in {f.Resources}")));
    }

    /// <summary>ConvertTo-Json yields an object for one result, an array for several, and nothing for none.</summary>
    public static List<(string Name, string Resources)> ParseDetections(string json)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var name = item.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var resources = item.TryGetProperty("Resources", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            list.Add((string.IsNullOrWhiteSpace(name) ? "an unnamed threat" : name!, string.IsNullOrWhiteSpace(resources) ? "an unknown location" : resources!));
        }
        return list;
    }
}
