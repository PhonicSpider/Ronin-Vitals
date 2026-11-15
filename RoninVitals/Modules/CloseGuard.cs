using System.ComponentModel;
using System.Windows;

namespace RoninVitals.Modules;

/// <summary>
/// Asks a question before a window closes, and closes it only when the answer is yes. A second close
/// request made while the question is still open is let through, so a question that never answers cannot
/// make the window impossible to close.
/// </summary>
public sealed class CloseGuard
{
    private readonly Window _window;
    private readonly Func<Task<bool>> _canClose;
    private bool _closeApproved;
    private bool _closeQuestionOpen;
    private bool _closed;

    public CloseGuard(Window window, Func<Task<bool>> canClose)
    {
        _window = window;
        _canClose = canClose;
        _window.Closing += OnClosing;
        _window.Closed += (_, _) => _closed = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved || e.Cancel) return;

        if (_closeQuestionOpen)
        {
            // Closing again while the first question is open is the user insisting. Let it through; the
            // pending answer is dropped when it arrives (see CloseWhenAnsweredAsync).
            _closeApproved = true;
            return;
        }

        e.Cancel = true;
        _closeQuestionOpen = true;
        _ = CloseWhenAnsweredAsync();
    }

    private async Task CloseWhenAnsweredAsync()
    {
        var canClose = true;
        try
        {
            canClose = await _canClose();
        }
        catch (Exception)
        {
            // A broken question counts as "yes" so the user is never trapped.
        }

        _closeQuestionOpen = false;
        if (!canClose) return;

        // Close() must not run inside the Closing handler. An already answered question continues here
        // synchronously, still inside that handler, so hand the close to the dispatcher to run once the
        // handler has returned. By then a second close request may have closed the window already.
        await _window.Dispatcher.InvokeAsync(() =>
        {
            if (_closeApproved || _closed) return;
            _closeApproved = true;
            _window.Close();
        });
    }
}
