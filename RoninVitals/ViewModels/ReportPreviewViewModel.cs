using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoninVitals.Services;

namespace RoninVitals.ViewModels;

/// <summary>
/// A finished report shown inside the app. Nothing is written until the user chooses "Save this Report", so a report
/// they don't want never reaches the disk. The window shows <see cref="Html"/>, the same page a saved file contains.
/// </summary>
public sealed partial class ReportPreviewViewModel : ObservableObject
{
    private readonly DateTimeOffset _createdAt;

    public ReportPreviewViewModel(string title, string html, DateTimeOffset createdAt)
    {
        Title = title;
        Html = html;
        _createdAt = createdAt;
    }

    public string Title { get; }
    public string Html { get; }

    /// <summary>Raised when the user chooses "Go back"; whatever hosts the window removes it.</summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => StatusMessage.Length > 0;
    public string SaveButtonLabel => IsSaving ? "Saving…" : IsSaved ? "Saved" : "Save this Report";

    private bool CanSave() => !IsSaved && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return; // ExecuteAsync skips CanExecute, so a second call must not make a second file
        IsSaving = true;
        try
        {
            var path = await Task.Run(() => ReportService.Save(Html, Title, _createdAt));
            StatusMessage = $"Saved to {path}";
            IsSaved = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't save the report: {exception.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
