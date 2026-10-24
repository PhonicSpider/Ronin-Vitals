using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class SuspiciousFileRulesTests
{
    // ── Disguised executables ────────────────────────────────────────────────

    [Theory]
    [InlineData("invoice.pdf.exe")]
    [InlineData("Invoice.PDF.EXE")]
    [InlineData("photo.jpg.scr")]
    [InlineData("report.docx.js")]
    [InlineData("resume.doc.vbs")]
    [InlineData("invoice.pdf          .exe")]              // padded so the real extension scrolls out of view
    [InlineData(@"C:\Users\x\Downloads\statement.xlsx.bat")] // full paths work too
    public void IsDisguisedExecutable_TrueForDocumentNameWithProgramEnding(string name) =>
        Assert.True(SuspiciousFileRules.IsDisguisedExecutable(name));

    [Theory]
    [InlineData("setup.exe")]            // a plain program is not disguised
    [InlineData("invoice.pdf")]          // a plain document is not a program
    [InlineData("notes.txt")]
    [InlineData("my.app.v2.exe")]        // dots in the name, but the inner "extension" isn't a document type
    [InlineData("backup.zip.001")]       // document-like inner, but not an executable ending
    [InlineData("installer.msi")]
    [InlineData("readme.rtf.lnk")]       // regression: a harmless game shortcut was flagged "don't open it" on a real PC
    [InlineData("")]
    public void IsDisguisedExecutable_FalseForOrdinaryFiles(string name) =>
        Assert.False(SuspiciousFileRules.IsDisguisedExecutable(name));

    [Fact]
    public void IsDisguisedExecutable_TrueForRightToLeftOverride()
    {
        // "exe.pdf" typed after U+202E is DRAWN as "fdp.exe" reversed, hiding the real ending.
        Assert.True(SuspiciousFileRules.IsDisguisedExecutable("annual\u202Efdp.exe"));
    }

    // ── Startup command parsing ──────────────────────────────────────────────

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\run.exe /x /y", "C:\\Tools\\run.exe")]
    [InlineData("C:\\Tools\\run.exe", "C:\\Tools\\run.exe")]
    [InlineData("C:\\Some Folder\\thing.bat -a", "C:\\Some Folder\\thing.bat")]      // unquoted path with a space
    [InlineData("  \"C:\\x\\y.exe\"  ", "C:\\x\\y.exe")]
    public void ExtractExecutablePath_FindsTheProgram(string command, string expected) =>
        Assert.Equal(expected, SuspiciousFileRules.ExtractExecutablePath(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void ExtractExecutablePath_NullWhenNothingToLookAt(string? command) =>
        Assert.Null(SuspiciousFileRules.ExtractExecutablePath(command));

    // ── Risky launch locations ───────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\Sam\AppData\Local\Temp\update.exe")]
    [InlineData(@"C:\Windows\Temp\x.exe")]
    [InlineData(@"C:\Users\Sam\Downloads\tool.exe")]
    [InlineData(@"C:\Users\Public\helper.exe")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\a.exe")]
    [InlineData("c:/users/sam/downloads/tool.exe")]          // forward slashes and lower case
    public void IsRiskyLaunchLocation_TrueForTempDownloadsPublicRecycleBin(string path) =>
        Assert.True(SuspiciousFileRules.IsRiskyLaunchLocation(path));

    [Theory]
    [InlineData(@"C:\Program Files\Vendor\app.exe")]
    [InlineData(@"C:\Program Files (x86)\Vendor\app.exe")]
    [InlineData(@"C:\Users\Sam\AppData\Local\Programs\App\app.exe")]
    [InlineData(@"C:\Windows\System32\svchost.exe")]
    [InlineData(null)]
    [InlineData("")]
    public void IsRiskyLaunchLocation_FalseForNormalInstallLocations(string? path) =>
        Assert.False(SuspiciousFileRules.IsRiskyLaunchLocation(path));

    [Fact]
    public void IsRiskyLaunchLocation_DoesNotFlagAFolderMerelyContainingTheWordTemp()
    {
        // "Templates" and "Temperature" are not Temp folders; only a real \temp\ path segment counts.
        Assert.False(SuspiciousFileRules.IsRiskyLaunchLocation(@"C:\Program Files\Temperature Tools\t.exe"));
        Assert.False(SuspiciousFileRules.IsRiskyLaunchLocation(@"C:\Users\Sam\Templates\t.exe"));
    }
}
