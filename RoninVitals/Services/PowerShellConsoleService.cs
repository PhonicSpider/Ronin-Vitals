using System.Diagnostics;
using System.Text;

namespace RoninVitals.Services;

/// <summary>
/// Hosts one real, persistent PowerShell process for Tech Mode's console panel - not a
/// simulated or text-only log. Commands are written to its stdin and results read back from its
/// stdout, one at a time (the console UI disables input while a command is running, so there's
/// no concurrent-command case to handle).
///
/// Each command is wrapped as ONE compound statement - "Write-Output begin-marker; &lt;command&gt;;
/// Write-Output end-marker" - sent as a single stdin line, rather than the command and a
/// sentinel as two separate lines. That matters: a redirected PowerShell host still writes its
/// own prompt strings (and sometimes echoes what it read) to stdout between reading one stdin
/// line and the next, so sending the command and a trailing sentinel as two lines left a gap
/// where that noise could land *between* them and get folded into the captured output - which
/// is exactly what happened the first time this shipped (a stray "PS&gt;" prompt and echoed
/// input ended up prepended to a real command's result). Wrapping everything into one compound
/// statement closes that gap - nothing else can execute between the begin marker and the
/// command, or between the command and the end marker, because PowerShell runs a single input
/// line's statements back to back with nothing else able to interleave. Any prompt/echo noise
/// still shows up, but only *before* the begin marker, where it's deliberately discarded.
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
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoLogo -NoProfile",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start PowerShell.");
        return Task.FromResult(new PowerShellConsoleService(process));
    }

    /// <summary>Runs one command and returns its output, discarding anything before the begin
    /// marker (leftover prompt/echo noise) and stopping at the end marker. Call sequentially -
    /// one command at a time.</summary>
    public async Task<string> RunCommandAsync(string command)
    {
        var token = Guid.NewGuid().ToString("N");
        var begin = $"__RONIN_BEGIN_{token}__";
        var end = $"__RONIN_END_{token}__";

        await _process.StandardInput.WriteLineAsync($"Write-Output '{begin}'; {command}; Write-Output '{end}'");
        await _process.StandardInput.FlushAsync();

        // Exact match after trimming, not Contains - PowerShell's host echoes the raw input
        // line back for redirected/piped stdin, and that echoed line also *contains* the begin
        // marker as a substring (it's literally part of what was sent). A Contains check matches
        // that echo instead of the real Write-Output result that follows it, so the echo's own
        // marker text was ending up captured as part of the "output" - confirmed live: `Get-Date`
        // returned "__RONIN_BEGIN_...__\nSunday, August 9, 2026 ...". The real Write-Output
        // result is the marker and nothing else on its own line, which an exact match correctly
        // distinguishes from the longer echoed compound statement.
        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.Trim() == begin) break;
        }

        var output = new StringBuilder();
        while ((line = await _process.StandardOutput.ReadLineAsync()) is not null)
        {
            if (line.Trim() == end) break;
            output.AppendLine(line);
        }

        return output.ToString().TrimEnd();
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
        catch
        {
            // Best-effort cleanup - the process dying on its own or already being gone isn't
            // worth surfacing an error for here.
        }
        _process.Dispose();
    }
}
