using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Vam.Ui.State;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>
/// One answer to "what colour is this strip", because four places ask it: the strip, the channel
/// panel, the share band and the band's legend. They disagreeing is a band an operator cannot read.
/// </summary>
public class ChannelStateExtensionsTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AChosenColourWins()
    {
        ChannelState channel = new() { Index = 3, Colour = "#ff0000" };

        Assert.Equal("#ff0000", channel.ToStripColour());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", TestCategories.Unit)]
    public void NoChoiceFallsBackToThePaletteByIndex(string colour)
    {
        // Derived from the index rather than handed out in order, so the strip that was blue this
        // morning is blue this afternoon, on every console watching.
        ChannelState channel = new() { Index = 1, Colour = colour };

        Assert.Equal(StripPalette.For(1), channel.ToStripColour());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MoreStripsThanColoursStillGetsOne()
    {
        ChannelState channel = new() { Index = StripPalette.All.Count + 2 };

        Assert.Equal(StripPalette.For(2), channel.ToStripColour());
    }
}
