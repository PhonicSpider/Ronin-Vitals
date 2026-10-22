using System.Collections.ObjectModel;
using System.Windows;
using RoninVitals.Models;

namespace RoninVitals.Services;

/// <summary>
/// Session-scoped, plain-language log of every fix the app has applied - what was done, when,
/// and whether/how it can be undone. Not persisted to disk yet (session-only); see the build
/// checklist. Static like the other catalog/diagnostics services in this codebase, since it
/// needs to be reachable from deep in the check/fix view-model tree without threading a
/// reference all the way down.
/// </summary>
public static class ActionLogService
{
    public static ObservableCollection<ActionLogEntry> Entries { get; } = new();

    /// <summary>Records an entry. Marshals onto the UI thread so callers on a background thread (fix delegates) can call this safely.</summary>
    public static void Record(ActionLogEntry entry)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            Entries.Insert(0, entry);
        }
        else
        {
            dispatcher.Invoke(() => Entries.Insert(0, entry));
        }
    }
}
