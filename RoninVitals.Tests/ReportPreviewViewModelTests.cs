using System.IO;
using RoninVitals.Services;
using RoninVitals.ViewModels;
using Xunit;

namespace RoninVitals.Tests;

/// <summary>The in-app report window. Shares ReportService's settable folder, so it runs one at a time.</summary>
[Collection(ReportServiceCollection.Name)]
public sealed class ReportPreviewViewModelTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 30, 15, 42, 7, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RoninVitalsPreviewTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _originalFolder = ReportService.ReportsFolder;

    public ReportPreviewViewModelTests() => ReportService.ReportsFolder = _folder;

    public void Dispose()
    {
        ReportService.ReportsFolder = _originalFolder;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ReportPreviewViewModel MakePreview() => new("Security report", "<html>what was found</html>", CreatedAt);

    [Fact]
    public void ShowingTheReport_SavesNothing()
    {
        _ = MakePreview();

        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void ItStartsUnsavedWithTheSaveButtonReady()
    {
        var preview = MakePreview();

        Assert.False(preview.IsSaved);
        Assert.Equal("Save this Report", preview.SaveButtonLabel);
        Assert.True(preview.SaveCommand.CanExecute(null));
        Assert.False(preview.HasStatusMessage);
    }

    [Fact]
    public async Task Save_WritesTheSameHtmlThatIsOnScreen()
    {
        var preview = MakePreview();

        await preview.SaveCommand.ExecuteAsync(null);

        var file = Assert.Single(Directory.GetFiles(_folder, "*.html"));
        Assert.Equal("<html>what was found</html>", File.ReadAllText(file));
        Assert.Equal("RoninVitals-security-report-2026-10-30-154207.html", Path.GetFileName(file));
    }

    [Fact]
    public async Task Save_TellsTheUserWhereTheFileWentAndMarksItSaved()
    {
        var preview = MakePreview();

        await preview.SaveCommand.ExecuteAsync(null);

        Assert.True(preview.IsSaved);
        Assert.True(preview.HasStatusMessage);
        Assert.StartsWith("Saved to ", preview.StatusMessage);
        Assert.Contains(_folder, preview.StatusMessage);
        Assert.Equal("Saved", preview.SaveButtonLabel);
    }

    [Fact]
    public async Task Save_CannotBeRunAgain_SoClickingTwiceDoesNotMakeASecondFile()
    {
        var preview = MakePreview();
        await preview.SaveCommand.ExecuteAsync(null);

        Assert.False(preview.SaveCommand.CanExecute(null));
        await preview.SaveCommand.ExecuteAsync(null);

        Assert.Single(Directory.GetFiles(_folder, "*.html"));
    }

    [Fact]
    public async Task Save_SaysSoWhenTheFolderCannotBeWritten_AndLetsTheUserTryAgain()
    {
        // A file where the reports folder should be makes the folder impossible to create.
        File.WriteAllText(_folder, "in the way");
        var preview = MakePreview();

        await preview.SaveCommand.ExecuteAsync(null);

        Assert.StartsWith("Couldn't save the report", preview.StatusMessage);
        Assert.False(preview.IsSaved);
        Assert.True(preview.SaveCommand.CanExecute(null));
        Assert.Equal("Save this Report", preview.SaveButtonLabel);
    }

    [Fact]
    public async Task Save_ClearsTheOldErrorOnceItWorks()
    {
        File.WriteAllText(_folder, "in the way");
        var preview = MakePreview();
        await preview.SaveCommand.ExecuteAsync(null);
        File.Delete(_folder);

        await preview.SaveCommand.ExecuteAsync(null);

        Assert.StartsWith("Saved to ", preview.StatusMessage);
    }

    [Fact]
    public void GoBack_ClosesTheWindowAndSavesNothing()
    {
        var preview = MakePreview();
        var closed = 0;
        preview.CloseRequested += (_, _) => closed++;

        preview.CloseCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void ItKeepsTheTitleAndHtmlForTheWindowToShow()
    {
        var preview = MakePreview();

        Assert.Equal("Security report", preview.Title);
        Assert.Equal("<html>what was found</html>", preview.Html);
    }
}
