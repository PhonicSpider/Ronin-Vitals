using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class CheckTextTests
{
    [Theory]
    [InlineData(1, "file")]
    [InlineData(0, "files")]
    [InlineData(2, "files")]
    public void Plural_PicksTheFormByCount(int count, string expected) =>
        Assert.Equal(expected, CheckText.Plural(count, "file", "files"));

    [Fact]
    public void Examples_ListsEverythingWhenItFits() =>
        Assert.Equal("a, b, c", CheckText.Examples(["a", "b", "c"]));

    [Fact]
    public void Examples_ShortensLongListsWithACount() =>
        Assert.Equal("a, b, c and 2 more", CheckText.Examples(["a", "b", "c", "d", "e"]));

    [Fact]
    public void Examples_IgnoresDuplicatesWhenCountingWhatWasLeftOut() =>
        Assert.Equal("a, b, c", CheckText.Examples(["a", "a", "b", "b", "c", "c"]));

    [Fact]
    public void Examples_HonoursACustomLimit() =>
        Assert.Equal("a and 2 more", CheckText.Examples(["a", "b", "c"], maxShown: 1));

    [Fact]
    public void DescribeException_NamesTheTypeAndMessage() =>
        Assert.Equal("InvalidOperationException: boom", CheckText.DescribeException(new InvalidOperationException("boom")));
}
