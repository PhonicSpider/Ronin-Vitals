using System.IO;
using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public sealed class BoundedFileWalkerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vitals-walker-" + Guid.NewGuid().ToString("N")[..8]);

    public BoundedFileWalkerTests()
    {
        // root/top.txt, root/one/middle.txt, root/one/two/bottom.txt
        Directory.CreateDirectory(Path.Combine(_root, "one", "two"));
        File.WriteAllText(Path.Combine(_root, "top.txt"), "");
        File.WriteAllText(Path.Combine(_root, "one", "middle.txt"), "");
        File.WriteAllText(Path.Combine(_root, "one", "two", "bottom.txt"), "");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ScanBudget Unlimited() => new(maxFiles: 1_000, TimeSpan.FromMinutes(1));

    private List<string> FileNames(int maxDepth, ScanBudget budget) =>
        BoundedFileWalker.EnumerateFiles([_root], maxDepth, budget).Select(Path.GetFileName).Order().ToList()!;

    [Theory]
    [InlineData(0, new[] { "top.txt" })]
    [InlineData(1, new[] { "middle.txt", "top.txt" })]
    [InlineData(2, new[] { "bottom.txt", "middle.txt", "top.txt" })]
    public void EnumerateFiles_StopsAtTheDepthLimit(int maxDepth, string[] expected) =>
        Assert.Equal(expected, FileNames(maxDepth, Unlimited()));

    [Fact]
    public void EnumerateFiles_StopsAtTheFileLimitAndSaysSo()
    {
        var budget = new ScanBudget(maxFiles: 2, TimeSpan.FromMinutes(1));

        var found = BoundedFileWalker.EnumerateFiles([_root], maxDepth: 5, budget).ToList();

        Assert.Single(found); // the second file hits the limit before it is returned
        Assert.True(budget.Truncated);
    }

    [Fact]
    public void EnumerateFiles_ReportsNotTruncatedWhenEverythingWasCovered()
    {
        var budget = Unlimited();

        _ = FileNames(maxDepth: 5, budget);

        Assert.False(budget.Truncated);
        Assert.Equal(3, budget.FilesSeen);
    }

    [Fact]
    public void EnumerateFiles_SkipsMissingRootsAndDuplicates()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        var found = BoundedFileWalker.EnumerateFiles([missing, _root, _root.ToUpperInvariant()], maxDepth: 0, Unlimited()).ToList();

        Assert.Single(found);
    }

    [Fact]
    public void EnumerateFiles_StopsOnceTheTimeLimitPasses()
    {
        var budget = new ScanBudget(maxFiles: 1_000, TimeSpan.Zero);
        Thread.Sleep(20);

        var found = BoundedFileWalker.EnumerateFiles([_root], maxDepth: 5, budget).ToList();

        Assert.Empty(found);
        Assert.True(budget.Truncated);
    }
}
