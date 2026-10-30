using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class SystemMetricsServiceTests
{
    [Theory]
    [InlineData(21, 10, 52, "21:10:52:30")]
    [InlineData(0, 0, 0, "00:00:00:30")]
    [InlineData(0, 3, 7, "00:03:07:30")]
    [InlineData(5, 23, 59, "05:23:59:30")]
    [InlineData(100, 1, 1, "100:01:01:30")]
    public void FormatUptime_IsDaysHoursMinutesSeconds(int days, int hours, int minutes, string expected) =>
        Assert.Equal(expected, SystemMetricsService.FormatUptime(new TimeSpan(days, hours, minutes, 30)));

    [Fact]
    public void FormatUptime_ChangesEverySecond_SoItReadsAsALiveClock()
    {
        var atFourSeconds = SystemMetricsService.FormatUptime(new TimeSpan(1, 2, 3, 4));
        var atFiveSeconds = SystemMetricsService.FormatUptime(new TimeSpan(1, 2, 3, 5));
        Assert.Equal("01:02:03:04", atFourSeconds);
        Assert.Equal("01:02:03:05", atFiveSeconds);
    }

    [Fact]
    public void SampleUptimeText_ReturnsAWellFormedReadout()
    {
        var text = new SystemMetricsService().SampleUptimeText();
        Assert.Matches(@"^\d{2,}:\d{2}:\d{2}:\d{2}$", text);
    }
}
