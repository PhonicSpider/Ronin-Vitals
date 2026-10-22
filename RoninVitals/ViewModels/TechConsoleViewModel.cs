using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Models;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

/// <summary>
/// Backs the Tech Mode console panel for one expanded symptom card. Two kinds of lines end up
/// here: system lines appended as each diagnostic check completes (display only - no shell
/// round-trip, since most checks are in-process .NET/WMI calls with no literal command to run),
/// and technician-typed commands, which really do run through a live PowerShellConsoleService.
/// The shell process itself is created lazily on first real command, not on card expand - most
/// Tech Mode sessions will never type anything, and spawning powershell.exe isn't free.
/// </summary>
public partial class TechConsoleViewModel : ObservableObject, IDisposable
{
    private PowerShellConsoleService? _shell;

    public ObservableCollection<ConsoleLine> Lines { get; } = new();

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public void AppendSystemLine(string label, string resultText)
    {
        Lines.Add(new ConsoleLine(ConsoleLineKind.Command, "> " + label));
        Lines.Add(new ConsoleLine(ConsoleLineKind.Output, resultText));
    }

    public void Clear() => Lines.Clear();

    [RelayCommand]
    private async Task RunInputAsync()
    {
        var command = InputText.Trim();
        if (command.Length == 0 || IsBusy) return;

        InputText = string.Empty;
        IsBusy = true;
        Lines.Add(new ConsoleLine(ConsoleLineKind.Command, "> " + command));

        try
        {
            _shell ??= await PowerShellConsoleService.CreateAsync();
            var output = await _shell.RunCommandAsync(command);
            Lines.Add(new ConsoleLine(ConsoleLineKind.Output, output.Length == 0 ? "(no output)" : output));

            ActionLogService.Record(new ActionLogEntry
            {
                Description = $"Tech command: {command}",
                Timestamp = DateTime.Now,
                Note = $"Result: {Summarize(output)}",
            });
        }
        catch (Exception ex)
        {
            Lines.Add(new ConsoleLine(ConsoleLineKind.Output, $"Error: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Collapses possibly-multiline command output to one short line for the action
    /// log, which only has room for a single secondary line of text.</summary>
    private static string Summarize(string output)
    {
        const int maxLength = 200;
        var oneLine = output.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (oneLine.Length == 0) return "(no output)";
        return oneLine.Length > maxLength ? oneLine[..maxLength] + "…" : oneLine;
    }

    public void Dispose() => _shell?.Dispose();
}
