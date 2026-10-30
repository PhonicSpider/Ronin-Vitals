using System.Windows;
using RoninVitals.Services;

namespace RoninVitals;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated helper mode: relaunched by ElevatedHelperLauncher to do one admin-only job. Exits without showing a window.
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
