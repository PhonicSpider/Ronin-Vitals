using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RoninVitals.Services;

/// <summary>Result of running an external tool to completion.</summary>
internal sealed record ToolResult(int ExitCode, string Output, string Error, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs an external tool with no window, a hard timeout, and both output streams captured. A tool that hangs is
/// killed and reported as timed out, instead of freezing the check that started it.
/// </summary>
internal static class ProcessRunner
{
    internal static ToolResult Run(string fileName, string arguments, TimeSpan timeout, Encoding? outputEncoding = null) =>
        Run(new ProcessStartInfo { FileName = fileName, Arguments = arguments }, timeout, outputEncoding);

    internal static ToolResult Run(string fileName, IEnumerable<string> argumentList, TimeSpan timeout, Encoding? outputEncoding = null)
    {
        var startInfo = new ProcessStartInfo { FileName = fileName };
        foreach (var argument in argumentList) startInfo.ArgumentList.Add(argument);
        return Run(startInfo, timeout, outputEncoding);
    }

    private static ToolResult Run(ProcessStartInfo startInfo, TimeSpan timeout, Encoding? outputEncoding)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        if (outputEncoding is not null)
        {
            startInfo.StandardOutputEncoding = outputEncoding;
            startInfo.StandardErrorEncoding = outputEncoding;
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new ToolResult(-1, string.Empty, $"{startInfo.FileName} didn't start.", TimedOut: false);

            // Read both streams while waiting: a full pipe buffer would otherwise block the child forever.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* exited on its own just now */ }
                return new ToolResult(-1, string.Empty, "Timed out.", TimedOut: true);
            }

            return new ToolResult(process.ExitCode, outputTask.Result.Trim(), errorTask.Result.Trim(), TimedOut: false);
        }
        catch (Win32Exception exception)
        {
            return new ToolResult(-1, string.Empty, exception.Message, TimedOut: false);
        }
    }
}
