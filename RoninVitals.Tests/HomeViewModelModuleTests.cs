using RoninVitals.Models;
using RoninVitals.Modules;
using RoninVitals.ViewModels;
using Xunit;

namespace RoninVitals.Tests;

public class HomeViewModelModuleTests
{
    private static ModuleCard Card(string title) => new(title, "Subtitle", "M0,0", () => new object());

    private static HomeViewModel Create(IReadOnlyList<ModuleCard>? cards = null, Action<ModuleCard>? open = null) =>
        new(_ => { }, () => { }, cards, open);

    [Fact]
    public void ModuleCards_IsEmpty_WhenNoneGiven()
    {
        var vm = Create();

        Assert.Empty(vm.ModuleCards);
        Assert.False(vm.HasModuleCards);
    }

    [Fact]
    public void ModuleCards_KeepOrder()
    {
        var first = Card("First");
        var second = Card("Second");
        var third = Card("Third");

        var vm = Create([first, second, third]);

        Assert.True(vm.HasModuleCards);
        Assert.Equal([first, second, third], vm.ModuleCards);
    }

    [Fact]
    public void OpenModuleCardCommand_PassesTheCardToTheCallback()
    {
        var card = Card("Opened");
        ModuleCard? received = null;
        var vm = Create([card], c => received = c);

        vm.OpenModuleCardCommand.Execute(card);

        Assert.Same(card, received);
    }
}
