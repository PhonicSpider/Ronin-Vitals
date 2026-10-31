using System.Runtime.InteropServices;
using System.Text;

namespace RoninVitals.Services;

/// <summary>
/// Reads text out of a Windows resource reference such as <c>@%SystemRoot%\system32\foo.dll,-100</c>, the form Windows uses for
/// the descriptions of its own scheduled tasks and services. Uses the same shell call Explorer does.
/// </summary>
internal static class IndirectString
{
    private const int BufferLength = 1024;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint outputLength, IntPtr reserved);

    /// <summary>The text, or null when the reference can't be resolved (the file or string number doesn't exist).</summary>
    public static string? Resolve(string reference)
    {
        var output = new StringBuilder(BufferLength);
        try
        {
            return SHLoadIndirectString(reference, output, BufferLength, IntPtr.Zero) >= 0 ? output.ToString() : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
