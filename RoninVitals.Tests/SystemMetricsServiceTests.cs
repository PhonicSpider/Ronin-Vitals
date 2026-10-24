using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class SystemMetricsServiceTests
{
    [Theory]
    [InlineData(21, 10, 52, "21:10:52")]
    [InlineData(0, 0, 0, "00:00:00")]
    [InlineData(0, 3, 7, "00:03:07")]
    [InlineData(5, 23, 59, "05:23:59")]
    [InlineData(100, 1, 1, "100:01:01")]
    public void FormatUptime_IsDaysHoursMinutes(int days, int hours, int minutes, string expected) =>
        Assert.Equal(expected, SystemMetricsService.FormatUptime(new TimeSpan(days, hours, minutes, 30)));

    [Fact]
    public void FormatUptime_DropsSeconds_SoItOnlyChangesOnceAMinute()
    {
        var a = SystemMetricsService.FormatUptime(new TimeSpan(1, 2, 3, 0));
        var b = SystemMetricsService.FormatUptime(new TimeSpan(1, 2, 3, 59));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SampleUptimeText_ReturnsAWellFormedReadout()
    {
        var text = new SystemMetricsService().SampleUptimeText();
        Assert.Matches(@"^\d{2,}:\d{2}:\d{2}$", text);
    }
}
