using System.Diagnostics.Eventing.Reader;
using System.Text;
using Microsoft.Win32;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>Read-only checks that Windows' own files and the signed-in user's profile are healthy.</summary>
public static class SystemIntegrityService
{
    private static readonly TimeSpan SystemFileScanTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ComponentStoreTimeout = TimeSpan.FromMinutes(3);
    private const long ThirtyDaysInMilliseconds = 30L * 86_400_000;

    /// <summary>Runs System File Checker in verify-only mode. Takes several minutes and needs administrator rights; call from a background thread.</summary>
    public static CheckResult CheckSystemFiles()
    {
        const string techCommand = "sfc /verifyonly";
        if (!ElevationService.IsElevated)
            return new CheckResult(CheckStatus.Warn, "Verifying system files needs administrator rights.", techCommand, "The app isn't running as administrator.");

        // sfc writes UTF-16 text, unlike most tools.
        var run = ProcessRunner.Run("sfc.exe", "/verifyonly", SystemFileScanTimeout, Encoding.Unicode);
        if (run.TimedOut)
            return new CheckResult(CheckStatus.Warn, "The system file scan took longer than 20 minutes and was stopped.", techCommand, "Timed out.");

        return IntegrityRules.InterpretSfcOutput(run.Output) with { TechCommand = techCommand, TechDetail = LastLines(run.Output, 3) };
    }

    /// <summary>The quick DISM check of Windows' repair store. Seconds, not minutes, so it suits the broader health cards.</summary>
    public static CheckResult CheckComponentStore()
    {
        const string techCommand = "DISM /Online /Cleanup-Image /CheckHealth";
        var run = ProcessRunner.Run("dism.exe", "/Online /Cleanup-Image /CheckHealth", ComponentStoreTimeout);
        if (run.TimedOut)
            return new CheckResult(CheckStatus.Warn, "Checking Windows' component store took too long and was stopped.", techCommand, "Timed out.");

        return IntegrityRules.InterpretDismCheckHealth(run.Output, run.ExitCode) with { TechCommand = techCommand, TechDetail = LastLines(run.Output, 3) };
    }

    public static CheckResult CheckUserProfile()
    {
        const string techCommand = "Get-ChildItem 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ProfileList' | Where-Object PSChildName -like '*.bak'";
        try
        {
            var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var temporary = IntegrityRules.IsTemporaryProfileFolder(profilePath);
            var brokenKeys = CountBrokenProfileKeys();
            var recentErrors = EventLogDiagnosticsService.ReadEvents("Application",
                $"*[System[Provider[@Name='Microsoft-Windows-User Profiles Service'] and (EventID=1511 or EventID=1515) and TimeCreated[timediff(@SystemTime) <= {ThirtyDaysInMilliseconds}]]]").Count;

            var detail = $"Profile folder: {profilePath}. Damaged profile records (.bak): {brokenKeys}. Profile errors (events 1511/1515) in 30 days: {recentErrors}.";
            return IntegrityRules.JudgeProfile(temporary, brokenKeys, recentErrors) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (Exception exception) when (exception is EventLogException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't check your user profile.", techCommand, CheckText.DescribeException(exception));
        }
    }

    private static int CountBrokenProfileKeys()
    {
        using var profileList = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        return profileList?.GetSubKeyNames().Count(IntegrityRules.IsBrokenProfileKey) ?? 0;
    }

    /// <summary>Counts Microsoft policy values, which lock a setting in the Settings app and so can explain "it won't stay changed".</summary>
    public static CheckResult CheckPolicyControlledSettings()
    {
        const string techCommand = "Get-ChildItem 'HKLM:\\SOFTWARE\\Policies\\Microsoft','HKCU:\\SOFTWARE\\Policies\\Microsoft' -Recurse";
        try
        {
            var valuesByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            CountPolicyValues(Registry.LocalMachine, valuesByGroup);
            CountPolicyValues(Registry.CurrentUser, valuesByGroup);

            var detail = valuesByGroup.Count == 0
                ? "No values under SOFTWARE\\Policies\\Microsoft."
                : string.Join("; ", valuesByGroup.OrderByDescending(group => group.Value).Select(group => $"{group.Key}: {group.Value}"));
            return IntegrityRules.JudgePolicies(valuesByGroup) with { TechCommand = techCommand, TechDetail = detail };
        }
        catch (System.Security.SecurityException exception)
        {
            return new CheckResult(CheckStatus.Warn, "Couldn't read the policy settings.", techCommand, CheckText.DescribeException(exception));
        }
    }

    private static void CountPolicyValues(RegistryKey hive, Dictionary<string, int> valuesByGroup)
    {
        using var policies = TryOpenSubKey(hive, @"SOFTWARE\Policies\Microsoft");
        if (policies is null) return;

        foreach (var groupName in policies.GetSubKeyNames())
        {
            using var group = TryOpenSubKey(policies, groupName);
            if (group is null) continue;

            var count = CountNamedValues(group);
            if (count > 0) valuesByGroup[groupName] = valuesByGroup.GetValueOrDefault(groupName) + count;
        }
    }

    private static int CountNamedValues(RegistryKey key)
    {
        var count = key.GetValueNames().Count(name => name.Length > 0);
        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = TryOpenSubKey(key, childName);
            if (child is not null) count += CountNamedValues(child);
        }
        return count;
    }

    /// <summary>Some policy keys can't be read without administrator rights; skipping one is better than failing the whole check.</summary>
    private static RegistryKey? TryOpenSubKey(RegistryKey parent, string name)
    {
        try { return parent.OpenSubKey(name); }
        catch (System.Security.SecurityException) { return null; }
    }

    private static string LastLines(string output, int count) =>
        string.Join(" ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));
}
