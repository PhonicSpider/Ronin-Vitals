using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoninVitals.Services;

/// <summary>
/// Finds where a running program lives on disk. Asks for the least access Windows has (query-limited), which is allowed
/// for most system processes even when the caller isn't an administrator, so it works where WMI and
/// <c>Process.MainModule</c> are refused.
/// </summary>
internal static class ProcessImagePath
{
    private const uint QueryLimitedInformation = 0x1000;
    private const int MaxPathLength = 32_768;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);

    /// <summary>The full path of the program's file, or null when the process has gone or Windows won't say.</summary>
    public static string? Find(int processId)
    {
        try
        {
            using var handle = OpenProcess(QueryLimitedInformation, inheritHandle: false, processId);
            if (handle.IsInvalid) return null;

            var path = new StringBuilder(1024);
            var size = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref size) && size <= MaxPathLength ? path.ToString() : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
