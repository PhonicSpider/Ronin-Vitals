using System.Diagnostics;
using System.Text;

namespace RoninVitals.Services;

/// <summary>
/// Runs one PowerShell command line to completion with a hard timeout and returns its output. Shared by the
/// checks that need a Windows cmdlet (Defender, firewall rules) so the process handling - hidden window,
/// UTF-8 output, both streams read at once to avoid a full-pipe deadlock, kill-on-timeout - lives in one place.
/// </summary>
internal static class PowerShellRunner
{
    internal sealed record ToolResult(int ExitCode, string Output, string Error, bool TimedOut);

    internal static ToolResult Run(string script, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + script);

        using var process = Process.Start(psi);
        if (process is null) return new ToolResult(-1, string.Empty, "powershell.exe didn't start.", false);

        // Read both streams while waiting, otherwise a full pipe buffer can deadlock the child.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
            return new ToolResult(-1, string.Empty, "Timed out.", true);
        }
        return new ToolResult(process.ExitCode, stdout.Result.Trim(), stderr.Result.Trim(), false);
    }
}
