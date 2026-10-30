using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RoninVitals.Services;

/// <summary>
/// One persistent PowerShell process behind Tech Mode's console, so variables and the current
/// folder carry over between commands. Run one command at a time; the console disables input
/// while a command is running.
/// </summary>
public sealed class PowerShellConsoleService : IDisposable
{
    private readonly Process _process;

    private PowerShellConsoleService(Process process)
    {
        _process = process;
    }

    public static Task<PowerShellConsoleService> CreateAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoLogo -NoProfile",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Couldn't start PowerShell.");

        // Errors are folded into the output (see BuildScript); this only stops the pipe from filling up and freezing the shell.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        return Task.FromResult(new PowerShellConsoleService(process));
    }

    /// <summary>Runs one command and returns what it printed, errors included.</summary>
    public async Task<string> RunCommandAsync(string command)
    {
        var token = Guid.NewGuid().ToString("N");
        var beginMarker = $"__RONIN_BEGIN_{token}__";
        var endMarker = $"__RONIN_END_{token}__";

        await _process.StandardInput.WriteLineAsync(BuildScript(command, beginMarker, endMarker));
        await _process.StandardInput.FlushAsync();

        // The host echoes the input line, which contains the marker text too, so only a line that is exactly the marker counts.
        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.Trim() == beginMarker) break;
        }

        var output = new StringBuilder();
        while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.Trim() == endMarker) break;
            output.AppendLine(line);
        }

        return output.ToString().TrimEnd();
    }

    /// <summary>
    /// Wraps the command as one input line so nothing can interleave between the markers. The command
    /// travels as Base64 and runs through Invoke-Expression inside try/catch: a typo that cannot parse
    /// becomes a printed error, not an unterminated line the shell waits on forever. The dot-sourced
    /// block keeps variables in the session scope while still letting 2&gt;&amp;1 catch the command's errors.
    /// </summary>
    private static string BuildScript(string command, string beginMarker, string endMarker)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        return $"Write-Output '{beginMarker}'; " +
               $"try {{ . {{ Invoke-Expression ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedCommand}'))) }} 2>&1 | Out-String -Stream }} " +
               "catch { $_.ToString() }; " +
               $"Write-Output '{endMarker}'";
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.WriteLine("exit");
                _process.StandardInput.Flush();
                if (!_process.WaitForExit(1000)) _process.Kill();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            // The process already exited or its pipe closed; nothing left to clean up.
        }
        _process.Dispose();
    }
}
