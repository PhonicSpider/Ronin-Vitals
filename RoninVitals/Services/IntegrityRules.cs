using System.IO;
using System.Text.RegularExpressions;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Reads the output of Windows' repair tools and judges user-profile signs. Pure, so it can be tested with sample output.</summary>
public static partial class IntegrityRules
{
    /// <summary>System File Checker in verify-only mode: nothing is repaired, only reported.</summary>
    public static CheckResult InterpretSfcOutput(string output)
    {
        if (output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Pass, "Windows' protected system files are all intact.");

        if (output.Contains("found integrity violations", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Bad, "Some of Windows' protected system files are damaged or changed.");

        if (output.Contains("repair pending", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Warn, "A system repair is waiting for a restart. Restart Windows, then run this check again.");

        return new CheckResult(CheckStatus.Warn, "The system file scan finished but Windows didn't give a clear answer.");
    }

    /// <summary>DISM's quick component-store check (reads flags Windows already keeps; it does not scan every file).</summary>
    public static CheckResult InterpretDismCheckHealth(string output, int exitCode)
    {
        if (exitCode == 740 || output.Contains("Elevated permissions are required", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Warn, "Checking Windows' component store needs administrator rights.");

        if (output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Pass, "Windows' component store, where it keeps its own repair files, has no damage recorded.");

        if (output.Contains("is repairable", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Bad, "Windows' component store is damaged, but Windows can repair it.");

        if (output.Contains("not repairable", StringComparison.OrdinalIgnoreCase))
            return new CheckResult(CheckStatus.Bad, "Windows' component store is damaged and can't repair itself. A repair install of Windows is likely needed.");

        return new CheckResult(CheckStatus.Warn, "Windows didn't give a clear answer about its component store.");
    }

    /// <summary>When Windows can't load a profile it signs you in to a temporary one in a folder called TEMP, which discards changes at sign-out.</summary>
    public static bool IsTemporaryProfileFolder(string profilePath)
    {
        var folderName = Path.GetFileName(profilePath.TrimEnd('\\', '/'));
        return folderName.Equals("TEMP", StringComparison.OrdinalIgnoreCase) || TempWithSuffixPattern().IsMatch(folderName);
    }

    /// <summary>A profile registry key ending in ".bak" is what Windows leaves behind when a profile fails to load.</summary>
    public static bool IsBrokenProfileKey(string profileListKeyName) =>
        profileListKeyName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);

    public static CheckResult JudgeProfile(bool usingTemporaryProfile, int brokenProfileKeys, int recentProfileErrors)
    {
        if (usingTemporaryProfile)
            return new CheckResult(CheckStatus.Bad, "You're signed in with a temporary profile, so settings and files you change are lost when you sign out.");

        if (brokenProfileKeys > 0 || recentProfileErrors > 0)
        {
            var signs = new List<string>();
            if (brokenProfileKeys > 0) signs.Add($"{brokenProfileKeys} damaged profile {CheckText.Plural(brokenProfileKeys, "record", "records")}");
            if (recentProfileErrors > 0) signs.Add($"{recentProfileErrors} profile {CheckText.Plural(recentProfileErrors, "error", "errors")} in the last 30 days");
            return new CheckResult(CheckStatus.Warn, $"Your profile shows signs of trouble: {string.Join(" and ", signs)}.");
        }

        return new CheckResult(CheckStatus.Pass, "Your user profile loads normally with no signs of damage.");
    }

    public static CheckResult JudgePolicies(IReadOnlyDictionary<string, int> valuesByGroup)
    {
        var windowsSettings = valuesByGroup.Where(group => group.Key.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)).Sum(group => group.Value);
        if (windowsSettings == 0)
            return new CheckResult(CheckStatus.Pass, "No policies are locking any Windows settings.");

        var groups = CheckText.Examples(valuesByGroup.OrderByDescending(group => group.Value).Select(group => group.Key), 4);
        return new CheckResult(CheckStatus.Warn,
            $"{windowsSettings} Windows {CheckText.Plural(windowsSettings, "setting is", "settings are")} set by policy rather than in Settings ({groups}). A setting controlled this way can look like it won't stay changed.");
    }

    [GeneratedRegex(@"^TEMP\.", RegexOptions.IgnoreCase)]
    private static partial Regex TempWithSuffixPattern();
}
