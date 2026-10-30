using RoninVitals.Services;
using Xunit;

namespace RoninVitals.Tests;

public class DriveFragmentationServiceTests
{
    private const int ExitUnknown = 2;

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("C:\\")]
    [InlineData("CC:")]
    [InlineData("C:' OR DriveLetter LIKE '%")]
    public void RunElevatedAnalyze_RejectsAnythingThatIsNotABareDriveLetter(string driveLetter) =>
        Assert.Equal(ExitUnknown, DriveFragmentationService.RunElevatedAnalyze(driveLetter));
}
