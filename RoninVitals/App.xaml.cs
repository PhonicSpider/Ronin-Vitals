using System.Windows;
using RoninVitals.Services;

namespace RoninVitals;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated helper mode: relaunched by RestorePointService with a UAC prompt for just
        // this one action. Do the WMI call and exit immediately - never shows a window, never
        // runs the rest of the app elevated.
        if (e.Args.Length >= 2 && e.Args[0] == RestorePointService.ElevatedArg)
        {
            Environment.Exit(RestorePointService.RunElevatedCreateRestorePoint(e.Args[1]));
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == DriveFragmentationService.ElevatedArg)
        {
            Environment.Exit(DriveFragmentationService.RunElevatedAnalyze(e.Args[1]));
            return;
        }

        base.OnStartup(e);
    }
}
