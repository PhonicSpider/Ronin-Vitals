using System.Diagnostics;

namespace RoninVitals.Modules;

/// <summary>Holds the modules found at startup so the rest of the app can read them without loading twice.</summary>
public static class ModuleHost
{
    private static IReadOnlyList<IFeatureModule> _discovered = [];

    /// <summary>The modules that loaded at startup. Empty when no module file is present.</summary>
    public static IReadOnlyList<IFeatureModule> Discovered => _discovered;

    /// <summary>Loads the modules from <paramref name="folder"/> once and writes a line for each one that was skipped.</summary>
    public static void Load(string folder)
    {
        var result = ModuleLoader.LoadFromFolder(folder);
        foreach (var problem in result.Problems)
        {
            Trace.TraceWarning(problem);
        }
        _discovered = result.Modules;
    }
}
