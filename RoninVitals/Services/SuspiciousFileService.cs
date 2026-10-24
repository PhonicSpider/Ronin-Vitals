using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Vitals' own read-only look for suspicious-looking files, the counterpart to the Microsoft Defender
/// scan in <see cref="SecurityScanService"/>. It finds things that are *worth a second look* (a program
/// pretending to be a PDF, an unsigned installer someone dropped in Downloads, something set to start
/// with Windows from a Temp folder). It cannot tell malware from a harmless program, and the wording of
/// every result says so instead of claiming a verdict. Nothing here deletes, moves or runs anything.
/// Folder walks are bounded by depth, file count and time so a huge Documents folder cannot stall a check.
/// </summary>
public static class SuspiciousFileService
{
    private static string DownloadsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    private static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static string TempFolder => Path.GetTempPath().TrimEnd('\\');

    private static readonly HashSet<string> SignedProgramExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".scr", ".msi" };

    private sealed class ScanBudget
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _maxFiles;
        private readonly TimeSpan _maxTime;
        public ScanBudget(int maxFiles, TimeSpan maxTime) { _maxFiles = maxFiles; _maxTime = maxTime; }
        public int FilesSeen { get; set; }
        public bool Truncated { get; private set; }
        public bool Exhausted
        {
            get
            {
                if (FilesSeen >= _maxFiles || _clock.Elapsed > _maxTime) Truncated = true;
                return Truncated;
            }
        }
    }

    private static IEnumerable<string> WalkFiles(IEnumerable<string> roots, int maxDepth, ScanBudget budget)
    {
        var stack = new Stack<(string Dir, int Depth)>();
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            stack.Push((root, 0));

        while (stack.Count > 0)
        {
            if (budget.Exhausted) yield break;
            var (dir, depth) = stack.Pop();

            string[] files;
            string[] subdirs;
            try
            {
                files = Directory.GetFiles(dir);
                subdirs = depth < maxDepth ? Directory.GetDirectories(dir) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue; // a folder we can't read isn't a finding, just a gap
            }

            foreach (var file in files)
            {
                budget.FilesSeen++;
                if (budget.Exhausted) yield break;
                yield return file;
            }

            foreach (var sub in subdirs)
            {
                try
                {
                    // Junctions/symlinks can loop back on themselves or point somewhere huge.
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
                stack.Push((sub, depth + 1));
            }
        }
    }

    /// <summary>The right-to-left override character would make the UI draw the name reversed, which
    /// is exactly the trick being reported, so show it as a visible marker instead.</summary>
    private static string Display(string path) => Path.GetFileName(path).Replace("\u202E", "<RLO>");

    private static string Examples(IEnumerable<string> names, int max = 3)
    {
        var list = names.Distinct().ToList();
        var shown = string.Join(", ", list.Take(max));
        return list.Count > max ? $"{shown} and {list.Count - max} more" : shown;
    }

    private static string Plural(int n, string one, string many) => n == 1 ? one : many;

    // ── Check 1: files pretending to be something else ──────────────────────────────────────────

    public static CheckResult CheckDisguisedFiles()
    {
        const string techCommand =
            @"Get-ChildItem $env:USERPROFILE\Downloads, [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), $env:TEMP -Recurse -Depth 3 -File | Where-Object { $_.Name -match '\.(pdf|docx?|xlsx?|pptx?|txt|jpe?g|png|gif|zip)\s*\.(exe|scr|bat|cmd|js|vbs|ps1)$' -or $_.Name -match '\u202E' }";
        try
        {
            var budget = new ScanBudget(maxFiles: 60_000, maxTime: TimeSpan.FromSeconds(40));
            var roots = new[] { DownloadsFolder, DesktopFolder, DocumentsFolder, TempFolder };
            var hits = WalkFiles(roots, maxDepth: 3, budget).Where(SuspiciousFileRules.IsDisguisedExecutable).ToList();

            var techDetail = $"Looked at {budget.FilesSeen:N0} files{(budget.Truncated ? " (stopped at the scan limit, so very large folders were only partly covered)" : "")}. " +
                             (hits.Count == 0 ? "None matched." : "Matches: " + string.Join(" | ", hits.Take(25).Select(h => h.Replace("\u202E", "<RLO>"))));

            return hits.Count == 0
                ? new CheckResult(CheckStatus.Pass, "No programs pretending to be documents or pictures (like invoice.pdf.exe) in your Downloads, Desktop, Documents or Temp folders.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Bad,
                    $"{hits.Count} {Plural(hits.Count, "file looks", "files look")} like a document or picture but {Plural(hits.Count, "is actually a program", "are actually programs")} (for example {Examples(hits.Select(Display))}). Don't open {Plural(hits.Count, "it", "them")}.",
                    techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish looking for disguised files.", techCommand, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Check 2: recent programs with no valid digital signature ────────────────────────────────

    public static CheckResult CheckUnsignedPrograms()
    {
        const string techCommand =
            @"Get-ChildItem $env:USERPROFILE\Downloads, [Environment]::GetFolderPath('Desktop'), $env:TEMP -Recurse -Depth 2 -Include *.exe,*.scr,*.msi -File | Where-Object LastWriteTime -gt (Get-Date).AddDays(-30) | Get-AuthenticodeSignature | Where-Object Status -ne 'Valid'";
        const int recentDays = 30;
        const int maxToVerify = 300;
        try
        {
            var cutoff = DateTime.Now.AddDays(-recentDays);
            var budget = new ScanBudget(maxFiles: 40_000, maxTime: TimeSpan.FromSeconds(40));
            var roots = new[] { DownloadsFolder, DesktopFolder, TempFolder };
            var all = WalkFiles(roots, maxDepth: 2, budget)
                .Where(f => SignedProgramExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (Path: f, Written: TryGetLastWrite(f)))
                .Where(f => f.Written >= cutoff)
                .ToList();

            // Verify the newest first: if there are more than the cap (a busy Temp folder easily has), the
            // freshest files are the ones worth checking, and the result must say it was capped.
            var candidates = all.OrderByDescending(f => f.Written).Take(maxToVerify).Select(f => f.Path).ToList();
            var capped = all.Count > candidates.Count;

            var unsigned = new List<(string Path, uint Code)>();
            foreach (var file in candidates)
            {
                var code = Authenticode.Verify(file);
                if (code != 0) unsigned.Add((file, code));
            }

            var scope = capped ? $"the {candidates.Count} newest of {all.Count}" : $"all {candidates.Count}";
            var techDetail = $"Checked {scope} program(s) changed in the last {recentDays} days. " +
                             (unsigned.Count == 0
                                 ? "All have a valid signature."
                                 : "No valid signature: " + string.Join(" | ", unsigned.Take(10).Select(u => DescribeUnsigned(u.Path, u.Code))));

            return unsigned.Count == 0
                ? new CheckResult(CheckStatus.Pass,
                    capped
                        ? $"The {candidates.Count} newest programs from the last {recentDays} days in your Downloads, Desktop and Temp folders all have a valid digital signature."
                        : $"Every program from the last {recentDays} days in your Downloads, Desktop and Temp folders has a valid digital signature.",
                    techCommand, techDetail)
                : new CheckResult(CheckStatus.Warn,
                    $"{unsigned.Count} {Plural(unsigned.Count, "program", "programs")} from the last {recentDays} days in Downloads, Desktop or Temp {Plural(unsigned.Count, "has", "have")} no valid digital signature (for example {Examples(unsigned.Select(u => Display(u.Path)))}). That doesn't mean {Plural(unsigned.Count, "it's", "they're")} unsafe, since plenty of small or older programs aren't signed, but make sure you know where {Plural(unsigned.Count, "it", "each one")} came from.",
                    techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish checking program signatures.", techCommand, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DateTime TryGetLastWrite(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return DateTime.MinValue; }
    }

    /// <summary>Path, size, date, trust-result code and a SHA-256 (for files under 100 MB) - the hash is
    /// what a technician would paste into a reputation lookup such as VirusTotal's search box.</summary>
    private static string DescribeUnsigned(string path, uint code)
    {
        string hash = "hash skipped";
        string size = "?";
        try
        {
            var info = new FileInfo(path);
            size = $"{info.Length / 1024.0 / 1024.0:0.0} MB";
            if (info.Length <= 100L * 1024 * 1024)
            {
                using var stream = File.OpenRead(path);
                hash = "SHA-256 " + Convert.ToHexString(SHA256.HashData(stream));
            }
            else hash = "hash skipped (over 100 MB)";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { hash = "hash unavailable (file in use)"; }

        return $"{path} ({size}, trust result 0x{code:X8}, {hash})";
    }

    // ── Check 3: startup entries launching from unusual places ──────────────────────────────────

    public static CheckResult CheckStartupEntries()
    {
        const string techCommand =
            @"Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Get-ChildItem ""$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup"", ""$env:ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp""";
        try
        {
            var flagged = new List<(string Where, string Name, string Command)>();
            var examined = 0;

            foreach (var (hive, label, subKey) in new[]
            {
                (Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Registry.LocalMachine, "HKLM (32-bit)", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            })
            {
                using var key = hive.OpenSubKey(subKey);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    examined++;
                    if (key.GetValue(name) is not string command) continue;
                    if (SuspiciousFileRules.IsRiskyLaunchLocation(SuspiciousFileRules.ExtractExecutablePath(command)))
                        flagged.Add((label + @"\Run", name, command));
                }
            }

            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder))
                {
                    examined++;
                    // A shortcut (.lnk) is the normal way to autostart something; a script or program sitting
                    // directly in the Startup folder is not.
                    var ext = Path.GetExtension(file);
                    if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (SuspiciousFileRules.ExecutableExtensions.Contains(ext))
                        flagged.Add(("Startup folder", Path.GetFileName(file), file));
                }
            }

            var techDetail = $"Looked at {examined} startup entr{(examined == 1 ? "y" : "ies")}. " +
                             (flagged.Count == 0 ? "None launch from Temp, Downloads, Public or the Recycle Bin." : "Flagged: " + string.Join(" | ", flagged.Select(f => $"{f.Where}: {f.Name} = {f.Command}")));

            return flagged.Count == 0
                ? new CheckResult(CheckStatus.Pass, "Nothing is set to start with Windows from a Temp, Downloads or other unusual folder.", techCommand, techDetail)
                : new CheckResult(CheckStatus.Warn,
                    $"{flagged.Count} startup {Plural(flagged.Count, "entry launches", "entries launch")} {Plural(flagged.Count, "a program", "programs")} from Temp, Downloads or another unusual place (for example {Examples(flagged.Select(f => f.Name))}). Legitimate software normally starts from Program Files, so this is worth checking.",
                    techCommand, techDetail);
        }
        catch (Exception ex)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't finish checking your startup entries.", techCommand, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Digital signature verification via Windows' own trust provider ──────────────────────────

    /// <summary>
    /// Wraps WinVerifyTrust (what Explorer's file Properties > Digital Signatures tab is built on).
    /// Revocation checks are off and URL retrieval is cache-only, so verifying a file never reaches out
    /// to the network and a scan stays fast and offline-safe.
    /// </summary>
    private static class Authenticode
    {
        private static Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

        private const uint WtdUiNone = 2;
        private const uint WtdChoiceFile = 1;
        private const uint WtdRevokeNone = 0;
        private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

        /// <summary>0 means a valid signature; anything else is a Windows trust error code (for example
        /// 0x800B0100 = not signed at all). A failure to even run the check counts as "not verified".</summary>
        public static uint Verify(string path)
        {
            var fileInfo = new WinTrustFileInfo { pcwszFilePath = path };
            fileInfo.cbStruct = (uint)Marshal.SizeOf(fileInfo);
            var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, pFile, false);
                var data = new WinTrustData
                {
                    dwUIChoice = WtdUiNone,
                    fdwRevocationChecks = WtdRevokeNone,
                    dwUnionChoice = WtdChoiceFile,
                    pFile = pFile,
                    dwProvFlags = WtdCacheOnlyUrlRetrieval,
                };
                data.cbStruct = (uint)Marshal.SizeOf(data);
                // INVALID_HANDLE_VALUE as the window handle = never show UI.
                return WinVerifyTrust(new IntPtr(-1), ref GenericVerifyV2, ref data);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException)
            {
                return uint.MaxValue;
            }
            finally
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(pFile);
                Marshal.FreeHGlobal(pFile);
            }
        }
    }
}
