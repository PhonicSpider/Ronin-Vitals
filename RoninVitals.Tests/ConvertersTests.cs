using System.Windows;
using RoninVitals.Converters;

namespace RoninVitals.Tests;

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = new();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Convert_InvertsBooleans(bool input, bool expected)
        => Assert.Equal(expected, _converter.Convert(input, typeof(bool), null!, null!));

    [Fact]
    public void Convert_NonBoolValueTreatedAsFalse()
        => Assert.Equal(true, _converter.Convert("not a bool", typeof(bool), null!, null!));

    [Fact]
    public void Convert_NullTreatedAsFalse()
        => Assert.Equal(true, _converter.Convert(null, typeof(bool), null!, null!));
}

public class InverseBoolToVisibilityConverterTests
{
    private readonly InverseBoolToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_TrueBecomesCollapsed()
        => Assert.Equal(Visibility.Collapsed, _converter.Convert(true, typeof(Visibility), null!, null!));

    [Fact]
    public void Convert_FalseBecomesVisible()
        => Assert.Equal(Visibility.Visible, _converter.Convert(false, typeof(Visibility), null!, null!));

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
        => Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(Visibility.Visible, typeof(bool), null!, null!));
}

public class BoolToStringConverterTests
{
    private readonly BoolToStringConverter _converter = new();

    [Fact]
    public void Convert_TrueReturnsFirstSegment()
        => Assert.Equal("Applied", _converter.Convert(true, typeof(string), "Applied|Apply", null!));

    [Fact]
    public void Convert_FalseReturnsSecondSegment()
        => Assert.Equal("Apply", _converter.Convert(false, typeof(string), "Applied|Apply", null!));

    [Fact]
    public void Convert_FalseWithNoSecondSegmentReturnsEmpty()
        => Assert.Equal(string.Empty, _converter.Convert(false, typeof(string), "OnlyTrueCase", null!));

    [Fact]
    public void Convert_NullParameterTreatedAsEmptyPipe()
        => Assert.Equal(string.Empty, _converter.Convert(true, typeof(string), null!, null!));
}
