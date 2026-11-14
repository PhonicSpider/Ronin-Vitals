using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace RoninVitals.Modules;

/// <summary>The modules that loaded, and a plain-language line for each one that did not.</summary>
public sealed record ModuleLoadResult(IReadOnlyList<IFeatureModule> Modules, IReadOnlyList<string> Problems);

/// <summary>Finds the optional module file next to the app and creates its modules. Nothing here throws out to the caller.</summary>
public static class ModuleLoader
{
    public const string ModuleFileName = "RoninVitals.Pro.dll";

    private static readonly object ResolveLock = new();
    private static readonly HashSet<string> ResolveFolders = new(StringComparer.OrdinalIgnoreCase);
    private static bool _resolverRegistered;

    /// <summary>Loads every module found in the module file inside <paramref name="folder"/>. An absent file is not a problem.</summary>
    public static ModuleLoadResult LoadFromFolder(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ModuleFileName);
            if (!File.Exists(path))
            {
                return Empty();
            }

            AddResolveFolder(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var assembly = Assembly.LoadFrom(path);
            var factories = assembly.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false }
                               && typeof(IFeatureModule).IsAssignableFrom(type)
                               && type.GetConstructor(Type.EmptyTypes) is not null)
                .Select(type => (Func<IFeatureModule>)(() => (IFeatureModule)Activator.CreateInstance(type)!))
                .ToList();
            return Create(factories);
        }
        catch (Exception)
        {
            return new ModuleLoadResult([], ["The optional module file could not be loaded, so it was skipped."]);
        }
    }

    /// <summary>Builds each module, skipping any that fail to construct or report themselves unavailable, and keeping factory order.</summary>
    internal static ModuleLoadResult Create(IEnumerable<Func<IFeatureModule>> factories)
    {
        var modules = new List<IFeatureModule>();
        var problems = new List<string>();
        foreach (var factory in factories)
        {
            try
            {
                var module = factory();
                if (module.IsAvailable)
                {
                    modules.Add(module);
                }
            }
            catch (Exception)
            {
                problems.Add("An optional module could not be started, so it was skipped.");
            }
        }
        return new ModuleLoadResult(modules, problems);
    }

    /// <summary>Initializes each module, dropping any that throw and keeping the rest.</summary>
    internal static ModuleLoadResult Initialize(IReadOnlyList<IFeatureModule> modules, ModuleContext context)
    {
        var kept = new List<IFeatureModule>();
        var problems = new List<string>();
        foreach (var module in modules)
        {
            try
            {
                module.Initialize(context);
                kept.Add(module);
            }
            catch (Exception)
            {
                problems.Add("An optional module failed to set itself up, so it was skipped.");
            }
        }
        return new ModuleLoadResult(kept, problems);
    }

    private static ModuleLoadResult Empty() => new([], []);

    private static void AddResolveFolder(string folder)
    {
        lock (ResolveLock)
        {
            ResolveFolders.Add(folder);
            if (_resolverRegistered)
            {
                return;
            }
            _resolverRegistered = true;
            AssemblyLoadContext.Default.Resolving += ResolveFromModuleFolder;
        }
    }

    /// <summary>Lets the module file's own dependencies load from the folder it sits in (managed dlls only).</summary>
    private static Assembly? ResolveFromModuleFolder(AssemblyLoadContext context, AssemblyName name)
    {
        string[] folders;
        lock (ResolveLock)
        {
            folders = ResolveFolders.ToArray();
        }

        foreach (var folder in folders)
        {
            var candidate = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                try
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
                catch (Exception)
                {
                    // Not a loadable managed assembly; let the next folder or the default failure handle it.
                }
            }
        }
        return null;
    }
}
