using System.IO;
using System.Text.RegularExpressions;

namespace RoninVitals.Services;

/// <summary>
/// The pure decision rules behind Vitals' own "suspicious file" check, kept free of any file system
/// or registry access so they can be unit-tested directly. These are heuristics: they flag things
/// worth a second look, they do not and cannot prove something is malware.
/// </summary>
public static class SuspiciousFileRules
{
    /// <summary>Extensions that run code when opened. Shortcuts (.lnk) are deliberately NOT here: Windows
    /// always hides that ending, so installers and games routinely create harmless shortcuts named like
    /// "readme.rtf.lnk", and flagging those produced a false "don't open it" on a real machine. A shortcut
    /// that points at a malicious program is still possible, but telling the two apart means reading what
    /// the shortcut targets, which this check does not do; the Defender scan covers it.</summary>
    public static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".com", ".bat", ".cmd", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".ps1", ".msi", ".hta", ".pif",
    };

    /// <summary>Extensions people expect to be harmless, which is why malware likes to imitate them.</summary>
    public static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".jpg", ".jpeg", ".png", ".gif", ".zip", ".mp3", ".mp4",
    };

    private static readonly string[] RiskyLaunchFolders =
    {
        @"\appdata\local\temp\", @"\windows\temp\", @"\temp\", @"\downloads\", @"\users\public\", @"\$recycle.bin\",
    };

    private static readonly Regex ExecutableInCommand = new(
        @"^(?<path>.+?\.(exe|bat|cmd|com|scr|vbs|js|ps1|msi|hta))(\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// True for a file that looks like a document or picture but actually runs code, the classic
    /// "invoice.pdf.exe" trick (Windows hides known extensions by default, so the user sees only
    /// "invoice.pdf"). Also true for any name containing the right-to-left override character
    /// (U+202E), which reverses how the rest of the name is drawn so the real extension can be hidden.
    /// </summary>
    public static bool IsDisguisedExecutable(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.Contains('\u202E')) return true;

        if (!ExecutableExtensions.Contains(Path.GetExtension(name))) return false;

        // TrimEnd: "invoice.pdf          .exe" pads with spaces so the real extension scrolls out of view.
        var inner = Path.GetExtension(Path.GetFileNameWithoutExtension(name).TrimEnd());
        return inner.Length > 0 && DocumentExtensions.Contains(inner.TrimEnd());
    }

    /// <summary>
    /// Pulls the program path out of a startup command line, e.g.
    /// <c>"C:\Program Files\App\app.exe" --silent</c> or <c>C:\Tools\run.exe /x</c>. Returns null when
    /// there is nothing path-like to look at.
    /// </summary>
    public static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var match = ExecutableInCommand.Match(text);
        if (match.Success) return match.Groups["path"].Value;

        var firstToken = text.Split(' ', 2)[0];
        return firstToken.Length > 0 ? firstToken : null;
    }

    /// <summary>
    /// Legitimate software installs under Program Files or AppData\Local\Programs and launches from there;
    /// something set to start with Windows from Temp, Downloads, a shared Public folder or the Recycle Bin
    /// is a common malware pattern (it dropped itself there and registered to survive a reboot).
    /// </summary>
    public static bool IsRiskyLaunchLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\').ToLowerInvariant();
        return RiskyLaunchFolders.Any(normalized.Contains);
    }
}
