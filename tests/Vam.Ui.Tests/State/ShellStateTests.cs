using Vam.TestKit.Harness;
using Vam.Ui.State;
using Xunit;

namespace Vam.Ui.Tests.State;

/// <summary>Where the add overlay opens, which decides whether adding an output can be found. #94.</summary>
public class ShellStateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheBusTileOpensTheAddOverlayAtItsBusSection()
    {
        ShellState shell = new();

        shell.OpenAddBus();

        Assert.Equal(OverlayId.Add, shell.Overlay);
        Assert.True(shell.IsAddingBus);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheInputTileOpensItAtTheChannelSectionEvenAfterTheBusTile()
    {
        ShellState shell = new();

        shell.OpenAddBus();
        shell.OpenAddChannel();

        Assert.Equal(OverlayId.Add, shell.Overlay);
        Assert.False(shell.IsAddingBus);
    }
}
