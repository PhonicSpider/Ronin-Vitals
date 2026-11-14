using System.IO;
using System.Windows;
using RoninVitals.Modules;
using Xunit;

namespace RoninVitals.Tests;

public class ModuleLoaderTests
{
    private sealed class FakeModule : IFeatureModule
    {
        public bool Available { get; set; } = true;
        public bool ThrowOnAvailability { get; set; }
        public bool ThrowOnInitialize { get; set; }
        public bool Initialized { get; private set; }

        public bool IsAvailable => ThrowOnAvailability ? throw new InvalidOperationException("broken") : Available;
        public IReadOnlyList<ModuleCard> Cards { get; set; } = [];
        public ResourceDictionary? Resources { get; set; }

        public void Initialize(ModuleContext context)
        {
            if (ThrowOnInitialize)
            {
                throw new InvalidOperationException("broken");
            }
            Initialized = true;
        }
    }

    [Fact]
    public void LoadFromFolder_ReturnsNothing_WhenFileIsMissing()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var result = ModuleLoader.LoadFromFolder(folder);

            Assert.Empty(result.Modules);
            Assert.Empty(result.Problems);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void LoadFromFolder_RecordsOneProblem_WhenFileIsNotAnAssembly()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, ModuleLoader.ModuleFileName), "not a dll");

            var result = ModuleLoader.LoadFromFolder(folder);

            Assert.Empty(result.Modules);
            Assert.Single(result.Problems);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Create_KeepsFactoryOrder()
    {
        var first = new FakeModule();
        var second = new FakeModule();

        var result = ModuleLoader.Create([() => first, () => second]);

        Assert.Equal(new IFeatureModule[] { first, second }, result.Modules);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Create_SkipsAModuleWhoseConstructorThrows()
    {
        var result = ModuleLoader.Create([
            () => throw new InvalidOperationException("broken"),
            () => new FakeModule()]);

        Assert.Single(result.Modules);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void Create_DropsModulesThatAreNotAvailable()
    {
        var result = ModuleLoader.Create([() => new FakeModule { Available = false }]);

        Assert.Empty(result.Modules);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Create_DropsAModuleWhoseAvailabilityThrows_AndRecordsAProblem()
    {
        var kept = new FakeModule();

        var result = ModuleLoader.Create([() => new FakeModule { ThrowOnAvailability = true }, () => kept]);

        Assert.Equal(new IFeatureModule[] { kept }, result.Modules);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void Initialize_DropsAModuleThatThrows_AndKeepsTheOthers()
    {
        var good = new FakeModule();
        var bad = new FakeModule { ThrowOnInitialize = true };
        var context = new ModuleContext { NavigateHome = () => { } };

        var result = ModuleLoader.Initialize([bad, good], context);

        Assert.Equal(new IFeatureModule[] { good }, result.Modules);
        Assert.Single(result.Problems);
        Assert.True(good.Initialized);
    }
}
