using System.IO;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public sealed class BrowserCachesTests : IDisposable
{
    private readonly string _cacheFolder = Path.Combine(Path.GetTempPath(), "vitals-cache-" + Guid.NewGuid().ToString("N")[..8]);

    public BrowserCachesTests() => Directory.CreateDirectory(Path.Combine(_cacheFolder, "nested", "deeper"));

    public void Dispose() => Directory.Delete(_cacheFolder, recursive: true);

    private void WriteFile(string relativePath, int bytes) =>
        File.WriteAllBytes(Path.Combine(_cacheFolder, relativePath), new byte[bytes]);

    [Theory]
    [InlineData(0L, "0 MB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(985L * 1024 * 1024, "985 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1.5 GB")]
    public void FormatSize_UsesMegabytesBelowAGigabyteAndGigabytesAbove(long bytes, string expected) =>
        Assert.Equal(expected, BrowserCaches.FormatSize(bytes));

    [Fact]
    public void SizeInBytes_AddsUpEveryFileIncludingNestedOnes()
    {
        WriteFile("a.bin", 100);
        WriteFile(Path.Combine("nested", "b.bin"), 200);
        WriteFile(Path.Combine("nested", "deeper", "c.bin"), 300);

        Assert.Equal(600, BrowserCaches.SizeInBytes(_cacheFolder));
    }

    [Fact]
    public void SizeInBytes_IsZeroForAMissingFolder() =>
        Assert.Equal(0, BrowserCaches.SizeInBytes(Path.Combine(_cacheFolder, "missing")));

    [Fact]
    public void ClearContents_RemovesFilesAndSubfoldersButKeepsTheFolder()
    {
        WriteFile("a.bin", 10);
        WriteFile(Path.Combine("nested", "deeper", "c.bin"), 10);

        BrowserCaches.ClearContents(_cacheFolder);

        Assert.True(Directory.Exists(_cacheFolder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_cacheFolder));
    }

    [Fact]
    public void ClearContents_LeavesALockedFileInPlaceInsteadOfFailing()
    {
        WriteFile("free.bin", 10);
        var lockedPath = Path.Combine(_cacheFolder, "locked.bin");
        File.WriteAllBytes(lockedPath, new byte[10]);

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            BrowserCaches.ClearContents(_cacheFolder);
        }

        Assert.False(File.Exists(Path.Combine(_cacheFolder, "free.bin")));
        Assert.True(File.Exists(lockedPath));
    }
}
