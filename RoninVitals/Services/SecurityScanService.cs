using System.Diagnostics;
using System.Management;
using System.Text.Json;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Drives Microsoft Defender rather than imitating an antivirus. Status and definition age are read-only. The quick scan
/// is Defender's own, and Defender applies its normal policy to what it finds; Vitals only reports the outcome and never
/// deletes or moves a file. The one fix downloads Microsoft's latest definitions. When another antivirus is active
/// Windows turns Defender off on purpose, and the checks say so instead of raising an alarm.
/// </summary>
public static class SecurityScanService
{
    private const int NeverUpdatedSignatureAge = 65535; // Defender's marker for "no definitions yet"
    private const int FreshDefinitionDays = 3;
    private const int StaleDefinitionDays = 7;
    private const string StatusTechCommand = "Get-MpComputerStatus | Select-Object AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, AMRunningMode, AntivirusSignatureAge";
    private const string StatusScript = "Get-MpComputerStatus | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AMRunningMode,AntivirusSignatureAge | ConvertTo-Json -Compress";

    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DetectionQueryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);

    private sealed record DefenderStatus(bool ServiceEnabled, bool AntivirusEnabled, bool RealTimeEnabled, string RunningMode, int SignatureAgeDays)
    {
        /// <summary>Defender is standing by because another security product is in charge.</summary>
        public bool IsStandingBy =>
            RunningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase) || RunningMode.Contains("EDR", StringComparison.OrdinalIgnoreCase);

        public bool IsRunning => ServiceEnabled && AntivirusEnabled && !IsStandingBy;
    }

    private sealed record OtherAntivirus(IReadOnlyList<string> Names, string Detail)
    {
        public bool IsPresent => Names.Count > 0;
        public string Text => string.Join(" and ", Names);
    }

    public sealed record Detection(string Name, string Resources);

    // ── Reading Defender and Windows Security Center ────────────────────────────────────────

    private static (DefenderStatus? Status, string Detail) ReadStatus()
    {
        var result = PowerShellRunner.Run(StatusScript, StatusTimeout);
        if (!result.Succeeded || result.Output.Length == 0)
            return (null, result.TimedOut ? "Timed out reading Defender status." : $"Get-MpComputerStatus failed: {FirstLineOf(result.Error)}");

        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            var signatureAge = root.TryGetProperty("AntivirusSignatureAge", out var ageElement) && ageElement.TryGetInt32(out var days) ? days : -1;
            var status = new DefenderStatus(
                ServiceEnabled: ReadFlag(root, "AMServiceEnabled"),
                AntivirusEnabled: ReadFlag(root, "AntivirusEnabled"),
                RealTimeEnabled: ReadFlag(root, "RealTimeProtectionEnabled"),
                RunningMode: ReadString(root, "AMRunningMode") ?? "Unknown",
                SignatureAgeDays: signatureAge);
            return (status, result.Output);
        }
        catch (JsonException exception)
        {
            return (null, $"Couldn't read Defender's answer: {exception.Message}");
        }
    }

    private static bool ReadFlag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string FirstLineOf(string text) => text.Split('\n', 2)[0].Trim();

    /// <summary>Non-Defender antivirus that Windows Security Center reports as switched on.</summary>
    private static OtherAntivirus ReadOtherActiveAntivirus()
    {
        try
        {
            var products = AntivirusDiagnosticsService.ReadProducts();
            var activeNames = products.Where(product => product.IsOn && !product.IsMicrosoft).Select(product => product.Name).ToList();
            return new OtherAntivirus(activeNames, AntivirusDiagnosticsService.DescribeForTechnician(products));
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or InvalidCastException)
        {
            return new OtherAntivirus([], $"Couldn't read Security Center: {exception.Message}");
        }
    }

    // ── Checks ──────────────────────────────────────────────────────────────────────────────

    public static CheckResult CheckDefenderStatus()
    {
        var (status, detail) = ReadStatus();
        if (status is null)
            return new CheckResult(CheckStatus.Warn, "Microsoft Defender isn't available here - another antivirus may be in charge of this PC. Scan with that program instead.", StatusTechCommand, detail);

        if (!status.IsRunning)
        {
            var otherAntivirus = ReadOtherActiveAntivirus();
            var techDetail = $"{detail} | {otherAntivirus.Detail}";
            if (otherAntivirus.IsPresent)
                return new CheckResult(CheckStatus.Warn,
                    $"{otherAntivirus.Text} is your antivirus, so Windows keeps Microsoft Defender switched off - that's normal. Vitals can only start Defender scans, so open {otherAntivirus.Text} and run its scan from there.",
                    StatusTechCommand, techDetail);

            return status.IsStandingBy
                ? new CheckResult(CheckStatus.Warn, "Another security product is in charge on this PC and Defender is standing by. Run that product's scan too.", StatusTechCommand, techDetail)
                : new CheckResult(CheckStatus.Bad, "Microsoft Defender is turned off and no other antivirus is protecting this PC, so nothing is watching for threats.", StatusTechCommand, techDetail);
        }

        return status.RealTimeEnabled
            ? new CheckResult(CheckStatus.Pass, "Microsoft Defender is on and watching in real time.", StatusTechCommand, detail)
            : new CheckResult(CheckStatus.Warn, "Defender's real-time protection is off, so threats aren't blocked as they appear. A scan can still find them.", StatusTechCommand, detail);
    }

    public static CheckResult CheckDefenderDefinitions()
    {
        var (status, detail) = ReadStatus();
        if (status is null || status.SignatureAgeDays < 0)
            return new CheckResult(CheckStatus.Warn, "Couldn't tell how recent Defender's virus definitions are.", StatusTechCommand, detail);

        if (!status.IsRunning)
        {
            var otherAntivirus = ReadOtherActiveAntivirus();
            var techDetail = $"{detail} | {otherAntivirus.Detail}";
            return otherAntivirus.IsPresent
                ? new CheckResult(CheckStatus.Pass, $"Doesn't apply here - {otherAntivirus.Text} is your antivirus and manages its own virus definitions.", StatusTechCommand, techDetail)
                : new CheckResult(CheckStatus.Warn, "Defender isn't running, so its virus definitions aren't being kept up to date.", StatusTechCommand, techDetail);
        }

        var ageInDays = status.SignatureAgeDays;
        if (ageInDays >= NeverUpdatedSignatureAge)
            return new CheckResult(CheckStatus.Bad, "Defender has never downloaded virus definitions, so a scan has nothing to compare against.", StatusTechCommand, $"AntivirusSignatureAge = {ageInDays} (never updated). Raw: {detail}");

        var updatedWhen = ageInDays switch { 0 => "today", 1 => "yesterday", _ => $"{ageInDays} days ago" };
        var ageDetail = $"AntivirusSignatureAge = {ageInDays} day(s). Raw: {detail}";
        return ageInDays switch
        {
            <= FreshDefinitionDays => new CheckResult(CheckStatus.Pass, $"Defender's virus definitions were updated {updatedWhen}.", StatusTechCommand, ageDetail),
            <= StaleDefinitionDays => new CheckResult(CheckStatus.Warn, $"Defender's virus definitions were last updated {updatedWhen} - a scan with old definitions can miss newer threats.", StatusTechCommand, ageDetail),
            _ => new CheckResult(CheckStatus.Bad, $"Defender's virus definitions are {ageInDays} days old - a scan with definitions this old can miss recent threats.", StatusTechCommand, ageDetail),
        };
    }

    /// <summary>SafeAutoFix: downloads Microsoft's latest Defender definitions (<c>Update-MpSignature</c>).</summary>
    public static Task<bool> UpdateDefenderDefinitionsAsync() =>
        Task.Run(() => PowerShellRunner.Run("Update-MpSignature", UpdateTimeout).Succeeded);

    public static CheckResult CheckDefenderQuickScan()
    {
        const string techCommand = "Start-MpScan -ScanType QuickScan; Get-MpThreatDetection | Where-Object InitialDetectionTime -ge <scan start>";

        var (status, statusDetail) = ReadStatus();
        if (status is null)
            return new CheckResult(CheckStatus.Warn, "Couldn't start a Defender scan - Defender doesn't seem to be available here.", techCommand, statusDetail);

        if (!status.IsRunning)
        {
            var otherAntivirus = ReadOtherActiveAntivirus();
            var techDetail = $"{statusDetail} | {otherAntivirus.Detail}";
            return otherAntivirus.IsPresent
                ? new CheckResult(CheckStatus.Warn, $"Vitals can't scan with Defender because {otherAntivirus.Text} is your antivirus. Open {otherAntivirus.Text} and run its scan from there.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Warn, "Defender isn't running, so there's nothing for Vitals to scan with. Turn it on in Windows Security, then run this again.", techCommand, techDetail);
        }

        var scanStartedAt = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        var scan = PowerShellRunner.Run("Start-MpScan -ScanType QuickScan", ScanTimeout);
        stopwatch.Stop();

        if (scan.TimedOut)
            return new CheckResult(CheckStatus.Warn, "Defender's quick scan is taking much longer than usual, so Vitals stopped waiting. It may still be running - check Windows Security.", techCommand, $"No result after {ScanTimeout.TotalMinutes:0} minutes.");
        if (!scan.Succeeded)
            return new CheckResult(CheckStatus.Warn, "Defender couldn't run the scan from here. You can start one yourself in Windows Security > Virus & threat protection.", techCommand, $"Start-MpScan failed: {FirstLineOf(scan.Error)}");

        var duration = $"{(int)stopwatch.Elapsed.TotalMinutes}m {stopwatch.Elapsed.Seconds}s";
        var detectionsSince = scanStartedAt.AddSeconds(-2).ToString("yyyy-MM-ddTHH:mm:ss"); // culture-neutral for PowerShell's [datetime]
        var detectionQuery = PowerShellRunner.Run(
            $"$since=[datetime]'{detectionsSince}'; $names=@{{}}; Get-MpThreat | ForEach-Object {{ $names[$_.ThreatID]=$_.ThreatName }}; " +
            "@(Get-MpThreatDetection | Where-Object { $_.InitialDetectionTime -ge $since } | ForEach-Object { [pscustomobject]@{ Name=$names[$_.ThreatID]; Resources=($_.Resources -join '; ') } }) | ConvertTo-Json -Compress",
            DetectionQueryTimeout);

        if (!detectionQuery.Succeeded)
            return new CheckResult(CheckStatus.Warn, "Defender finished its scan, but Vitals couldn't read the result. Check Windows Security > Protection history.", techCommand, $"Scan took {duration}. Result query failed: {FirstLineOf(detectionQuery.Error)}");

        var detections = ParseDetections(detectionQuery.Output);
        if (detections.Count == 0)
            return new CheckResult(CheckStatus.Pass, $"Defender's quick scan finished (it took {duration}) and found nothing.", techCommand, $"Scan took {duration}. No detections since {detectionsSince}.");

        var threatNames = detections.Select(detection => detection.Name).Distinct().ToList();
        var shownNames = string.Join(", ", threatNames.Take(3)) + (threatNames.Count > 3 ? $" and {threatNames.Count - 3} more" : "");
        return new CheckResult(CheckStatus.Bad,
            $"Defender's quick scan found {detections.Count} {(detections.Count == 1 ? "threat" : "threats")} ({shownNames}). It has already acted on what it was sure about.",
            techCommand,
            $"Scan took {duration}. Detections: " + string.Join(" | ", detections.Select(detection => $"{detection.Name} in {detection.Resources}")));
    }

    /// <summary>PowerShell's <c>ConvertTo-Json</c> gives an object for one result, an array for several, and nothing for none.</summary>
    public static List<Detection> ParseDetections(string json)
    {
        var detections = new List<Detection>();
        if (string.IsNullOrWhiteSpace(json)) return detections;

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : [document.RootElement];

        foreach (var item in items.Where(candidate => candidate.ValueKind == JsonValueKind.Object))
        {
            var name = ReadString(item, "Name");
            var resources = ReadString(item, "Resources");
            detections.Add(new Detection(
                Name: string.IsNullOrWhiteSpace(name) ? "an unnamed threat" : name,
                Resources: string.IsNullOrWhiteSpace(resources) ? "an unknown location" : resources));
        }
        return detections;
    }
}
