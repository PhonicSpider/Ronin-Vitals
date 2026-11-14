using RoninVitals.Modules;
using Xunit;

namespace RoninVitals.Tests;

public class ModuleCardTests
{
    [Fact]
    public void TryCreateScreen_ReturnsTheScreen()
    {
        var screen = new object();
        var card = new ModuleCard("Title", "Subtitle", "M0,0", () => screen);

        Assert.Same(screen, card.TryCreateScreen());
    }

    [Fact]
    public void TryCreateScreen_ReturnsNull_WhenFactoryThrows()
    {
        var card = new ModuleCard("Title", "Subtitle", "M0,0", () => throw new InvalidOperationException("broken"));

        Assert.Null(card.TryCreateScreen());
    }
}
