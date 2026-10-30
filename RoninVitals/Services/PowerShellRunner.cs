using System.Text;

namespace RoninVitals.Services;

/// <summary>Runs one PowerShell command line through <see cref="ProcessRunner"/> with UTF-8 output and errors that stop the script.</summary>
internal static class PowerShellRunner
{
    internal static ToolResult Run(string script, TimeSpan timeout)
    {
        string[] arguments =
        [
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-Command", "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + script,
        ];
        return ProcessRunner.Run("powershell.exe", arguments, timeout, Encoding.UTF8);
    }
}
