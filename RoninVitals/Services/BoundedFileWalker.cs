using System.Diagnostics;
using System.IO;

namespace RoninVitals.Services;

/// <summary>Limits on how much a folder walk may do, so a huge folder cannot stall a check.</summary>
internal sealed class ScanBudget(int maxFiles, TimeSpan maxDuration)
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public int FilesSeen { get; set; }

    /// <summary>True once a limit was hit, so results can say the scan was only partial.</summary>
    public bool Truncated { get; private set; }

    public bool IsExhausted
    {
        get
        {
            if (FilesSeen >= maxFiles || _stopwatch.Elapsed > maxDuration) Truncated = true;
            return Truncated;
        }
    }
}

internal static class BoundedFileWalker
{
    /// <summary>Lists files under the roots, down to <paramref name="maxDepth"/> levels, skipping unreadable folders and links.</summary>
    public static IEnumerable<string> EnumerateFiles(IEnumerable<string> roots, int maxDepth, ScanBudget budget)
    {
        var pending = new Stack<(string Folder, int Depth)>();
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            pending.Push((root, 0));

        while (pending.Count > 0)
        {
            if (budget.IsExhausted) yield break;
            var (folder, depth) = pending.Pop();

            string[] files;
            string[] subfolders;
            try
            {
                files = Directory.GetFiles(folder);
                subfolders = depth < maxDepth ? Directory.GetDirectories(folder) : [];
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue; // an unreadable folder is a gap, not a finding
            }

            foreach (var file in files)
            {
                budget.FilesSeen++;
                if (budget.IsExhausted) yield break;
                yield return file;
            }

            foreach (var subfolder in subfolders.Where(subfolder => !IsLink(subfolder)))
                pending.Push((subfolder, depth + 1));
        }
    }

    /// <summary>Junctions and symlinks can loop back on themselves or lead somewhere huge.</summary>
    private static bool IsLink(string folder)
    {
        try { return (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { return true; }
    }
}
