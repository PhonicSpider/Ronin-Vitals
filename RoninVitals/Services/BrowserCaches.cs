using System.Diagnostics;
using System.IO;

namespace RoninVitals.Services;

internal sealed record BrowserCacheFolder(string Browser, string ProcessName, string Path);

/// <summary>Finds, measures and empties the cache folders of the common browsers. Only caches: never passwords, cookies, history or logins.</summary>
internal static class BrowserCaches
{
    public static List<BrowserCacheFolder> Find()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var caches = new List<BrowserCacheFolder>();

        // Chromium browsers keep one Cache folder per profile under "User Data".
        (string Browser, string ProcessName, string UserDataFolder)[] chromiumBrowsers =
        [
            ("Edge", "msedge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
            ("Chrome", "chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
            ("Brave", "brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
        ];
        foreach (var (browser, processName, userDataFolder) in chromiumBrowsers.Where(entry => Directory.Exists(entry.UserDataFolder)))
        {
            foreach (var profileFolder in SafeSubfolders(userDataFolder).Where(IsChromiumProfile))
                AddIfExists(caches, browser, processName, Path.Combine(profileFolder, "Cache"));
        }

        AddIfExists(caches, "Opera GX", "opera", Path.Combine(localAppData, "Opera Software", "Opera GX Stable", "Cache"));
        AddIfExists(caches, "Opera", "opera", Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache"));

        var firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        foreach (var profileFolder in SafeSubfolders(firefoxProfiles))
            AddIfExists(caches, "Firefox", "firefox", Path.Combine(profileFolder, "cache2"));

        return caches;
    }

    /// <summary>Names of the browsers among <paramref name="caches"/> that are open right now.</summary>
    public static List<string> FindRunningBrowsers(IEnumerable<BrowserCacheFolder> caches) =>
        caches.Where(cache => Process.GetProcessesByName(cache.ProcessName).Length > 0)
            .Select(cache => cache.Browser)
            .Distinct()
            .ToList();

    public static long SizeInBytes(string folder)
    {
        long totalBytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try { totalBytes += new FileInfo(file).Length; }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { /* vanished or locked */ }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { /* a partial total is fine */ }
        return totalBytes;
    }

    /// <summary>Deletes what is inside the folder, keeping the folder. Anything locked or protected is skipped.</summary>
    public static void ClearContents(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            try { File.Delete(file); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* locked or protected */ }
        }
        foreach (var subfolder in SafeSubfolders(folder))
        {
            try { Directory.Delete(subfolder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* in use */ }
        }
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private static bool IsChromiumProfile(string folder)
    {
        var name = Path.GetFileName(folder);
        return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
    }

    private static void AddIfExists(List<BrowserCacheFolder> caches, string browser, string processName, string cacheFolder)
    {
        if (Directory.Exists(cacheFolder)) caches.Add(new BrowserCacheFolder(browser, processName, cacheFolder));
    }

    private static List<string> SafeSubfolders(string folder)
    {
        try { return Directory.Exists(folder) ? Directory.GetDirectories(folder).ToList() : []; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return []; }
    }
}
