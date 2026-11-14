using System.Diagnostics;
using System.Windows;
using RoninVitals.Modules;
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

        ModuleHost.Load(AppContext.BaseDirectory);
        MergeModuleResources();

        base.OnStartup(e);
    }

    /// <summary>Adds each module's styles and templates to the application. A bad dictionary is skipped, never fatal.</summary>
    private void MergeModuleResources()
    {
        foreach (var module in ModuleHost.Discovered)
        {
            try
            {
                if (module.Resources is { } resources)
                {
                    Resources.MergedDictionaries.Add(resources);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"An optional module's resources could not be added, so they were skipped: {ex.Message}");
            }
        }
    }
}
