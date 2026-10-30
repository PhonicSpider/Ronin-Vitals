using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;
using RoninVitals.Models;
using static RoninVitals.Services.CheckText;

namespace RoninVitals.Services;

/// <summary>
/// Vitals' own read-only look for files worth a second look: programs disguised as documents, recent unsigned programs,
/// and startup entries launching from odd places. It cannot tell malware from a harmless program, so results say
/// "worth checking", never "infected". It deletes, moves and runs nothing. Folder walks are bounded (see
/// <see cref="BoundedFileWalker"/>). Microsoft Defender's scan is in <see cref="SecurityScanService"/>.
/// </summary>
public static class SuspiciousFileService
{
    private const int DisguisedFileSearchDepth = 3;
    private const int UnsignedProgramSearchDepth = 2;
    private const int RecentProgramDays = 30;
    private const int MaxProgramsToVerify = 300;
    private const int MaxProgramsToDescribe = 10;
    private const long MaxHashedFileBytes = 100L * 1024 * 1024;
    private const string RightToLeftOverride = "‮";

    private static readonly TimeSpan WalkTimeLimit = TimeSpan.FromSeconds(40);
    private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".scr", ".msi" };

    private sealed record UnsignedProgram(string Path, uint TrustResult);
    private sealed record StartupFinding(string Location, string Name, string Command);

    private static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string TempFolder => Path.GetTempPath().TrimEnd('\\');

    /// <summary>The right-to-left override character is the trick being reported, so show it as a marker instead of letting it garble the text.</summary>
    private static string MakeOverrideVisible(string text) => text.Replace(RightToLeftOverride, "<RLO>");

    private static string DisplayFileName(string path) => MakeOverrideVisible(Path.GetFileName(path));

    // ── Programs disguised as documents ─────────────────────────────────────────────────────

    public static CheckResult CheckDisguisedFiles()
    {
        const string techCommand =
            @"Get-ChildItem $env:USERPROFILE\Downloads, [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), $env:TEMP -Recurse -Depth 3 -File | Where-Object { $_.Name -match '\.(pdf|docx?|xlsx?|pptx?|txt|jpe?g|png|gif|zip)\s*\.(exe|scr|bat|cmd|js|vbs|ps1)$' -or $_.Name -match '‮' }";
        try
        {
            var budget = new ScanBudget(maxFiles: 60_000, WalkTimeLimit);
            string[] searchFolders = [DownloadsFolder, DesktopFolder, DocumentsFolder, TempFolder];
            var disguisedFiles = BoundedFileWalker.EnumerateFiles(searchFolders, DisguisedFileSearchDepth, budget)
                .Where(SuspiciousFileRules.IsDisguisedExecutable)
                .ToList();

            var coverage = $"Looked at {budget.FilesSeen:N0} files{(budget.Truncated ? " (stopped at the scan limit, so very large folders were only partly covered)" : "")}.";
            var techDetail = disguisedFiles.Count == 0
                ? $"{coverage} None matched."
                : $"{coverage} Matches: {string.Join(" | ", disguisedFiles.Take(25).Select(MakeOverrideVisible))}";

            if (disguisedFiles.Count == 0)
                return new CheckResult(CheckStatus.Pass, "No programs pretending to be documents or pictures (like invoice.pdf.exe) in your Downloads, Desktop, Documents or Temp folders.", techCommand, techDetail);

            var count = disguisedFiles.Count;
            return new CheckResult(CheckStatus.Bad,
                $"{count} {Plural(count, "file looks", "files look")} like a document or picture but {Plural(count, "is actually a program", "are actually programs")} (for example {Examples(disguisedFiles.Select(DisplayFileName))}). Don't open {Plural(count, "it", "them")}.",
                techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish looking for disguised files.", techCommand, DescribeException(exception));
        }
    }

    // ── Recent programs without a valid digital signature ───────────────────────────────────

    public static CheckResult CheckUnsignedPrograms()
    {
        const string techCommand =
            @"Get-ChildItem $env:USERPROFILE\Downloads, [Environment]::GetFolderPath('Desktop'), $env:TEMP -Recurse -Depth 2 -Include *.exe,*.scr,*.msi -File | Where-Object LastWriteTime -gt (Get-Date).AddDays(-30) | Get-AuthenticodeSignature | Where-Object Status -ne 'Valid'";
        try
        {
            var recentSince = DateTime.Now.AddDays(-RecentProgramDays);
            var budget = new ScanBudget(maxFiles: 40_000, WalkTimeLimit);
            string[] searchFolders = [DownloadsFolder, DesktopFolder, TempFolder];
            var recentPrograms = BoundedFileWalker.EnumerateFiles(searchFolders, UnsignedProgramSearchDepth, budget)
                .Where(file => ProgramExtensions.Contains(Path.GetExtension(file)))
                .Select(file => (Path: file, LastWritten: GetLastWriteTimeOrMinimum(file)))
                .Where(program => program.LastWritten >= recentSince)
                .ToList();

            // Newest first: when there are more than the cap (a busy Temp folder easily has), the freshest are worth checking.
            var programsToVerify = recentPrograms.OrderByDescending(program => program.LastWritten).Take(MaxProgramsToVerify).Select(program => program.Path).ToList();
            var wasCapped = recentPrograms.Count > programsToVerify.Count;

            var unsignedPrograms = programsToVerify
                .Select(path => new UnsignedProgram(path, AuthenticodeVerifier.Verify(path)))
                .Where(program => program.TrustResult != AuthenticodeVerifier.ValidSignature)
                .ToList();

            var scope = wasCapped ? $"the {programsToVerify.Count} newest of {recentPrograms.Count}" : $"all {programsToVerify.Count}";
            var techDetail = $"Checked {scope} program(s) changed in the last {RecentProgramDays} days. " +
                             (unsignedPrograms.Count == 0
                                 ? "All have a valid signature."
                                 : "No valid signature: " + string.Join(" | ", unsignedPrograms.Take(MaxProgramsToDescribe).Select(DescribeUnsignedProgram)));

            if (unsignedPrograms.Count == 0)
                return new CheckResult(CheckStatus.Pass,
                    wasCapped
                        ? $"The {programsToVerify.Count} newest programs from the last {RecentProgramDays} days in your Downloads, Desktop and Temp folders all have a valid digital signature."
                        : $"Every program from the last {RecentProgramDays} days in your Downloads, Desktop and Temp folders has a valid digital signature.",
                    techCommand, techDetail);

            var count = unsignedPrograms.Count;
            return new CheckResult(CheckStatus.Warn,
                $"{count} {Plural(count, "program", "programs")} from the last {RecentProgramDays} days in Downloads, Desktop or Temp {Plural(count, "has", "have")} no valid digital signature (for example {Examples(unsignedPrograms.Select(program => DisplayFileName(program.Path)))}). That doesn't mean {Plural(count, "it's", "they're")} unsafe, since plenty of small or older programs aren't signed, but make sure you know where {Plural(count, "it", "each one")} came from.",
                techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish checking program signatures.", techCommand, DescribeException(exception));
        }
    }

    private static DateTime GetLastWriteTimeOrMinimum(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return DateTime.MinValue; }
    }

    /// <summary>Path, size, trust code and SHA-256 (what a technician pastes into a reputation lookup such as VirusTotal).</summary>
    private static string DescribeUnsignedProgram(UnsignedProgram program)
    {
        try
        {
            var file = new FileInfo(program.Path);
            var size = $"{file.Length / 1024.0 / 1024.0:0.0} MB";
            string hash;
            if (file.Length > MaxHashedFileBytes) hash = "hash skipped (over 100 MB)";
            else
            {
                using var stream = File.OpenRead(program.Path);
                hash = "SHA-256 " + Convert.ToHexString(SHA256.HashData(stream));
            }
            return $"{program.Path} ({size}, trust result 0x{program.TrustResult:X8}, {hash})";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return $"{program.Path} (trust result 0x{program.TrustResult:X8}, size and hash unavailable: file in use)";
        }
    }

    // ── Startup entries launching from unusual places ───────────────────────────────────────

    public static CheckResult CheckStartupEntries()
    {
        const string techCommand =
            @"Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Get-ChildItem ""$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"", ""$env:ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp""";
        try
        {
            var findings = new List<StartupFinding>();
            var entriesExamined = 0;

            (RegistryKey Hive, string Label, string SubKey)[] runKeys =
            [
                (Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, "HKLM (32-bit)", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            ];
            foreach (var (hive, label, subKey) in runKeys)
            {
                using var runKey = hive.OpenSubKey(subKey);
                if (runKey is null) continue;
                foreach (var entryName in runKey.GetValueNames())
                {
                    entriesExamined++;
                    if (runKey.GetValue(entryName) is not string command) continue;
                    if (SuspiciousFileRules.IsRiskyLaunchLocation(SuspiciousFileRules.ExtractExecutablePath(command)))
                        findings.Add(new StartupFinding($@"{label}\Run", entryName, command));
                }
            }

            string[] startupFolders = [Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)];
            foreach (var startupFolder in startupFolders.Where(Directory.Exists))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    entriesExamined++;
                    // A shortcut is the normal way to autostart something; a program or script sitting in the folder is not.
                    if (SuspiciousFileRules.ExecutableExtensions.Contains(Path.GetExtension(file)))
                        findings.Add(new StartupFinding("Startup folder", Path.GetFileName(file), file));
                }
            }

            var techDetail = $"Looked at {entriesExamined} startup entr{(entriesExamined == 1 ? "y" : "ies")}. " +
                             (findings.Count == 0
                                 ? "None launch from Temp, Downloads, Public or the Recycle Bin."
                                 : "Flagged: " + string.Join(" | ", findings.Select(finding => $"{finding.Location}: {finding.Name} = {finding.Command}")));

            if (findings.Count == 0)
                return new CheckResult(CheckStatus.Pass, "Nothing is set to start with Windows from a Temp, Downloads or other unusual folder.", techCommand, techDetail);

            var count = findings.Count;
            return new CheckResult(CheckStatus.Warn,
                $"{count} startup {Plural(count, "entry launches", "entries launch")} {Plural(count, "a program", "programs")} from Temp, Downloads or another unusual place (for example {Examples(findings.Select(finding => finding.Name))}). Legitimate software normally starts from Program Files, so this is worth checking.",
                techCommand, techDetail);
        }
        catch (Exception exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish checking your startup entries.", techCommand, DescribeException(exception));
        }
    }
}
