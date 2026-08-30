using Vam.Engine.Metering;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Metering;

/// <summary>
/// F1's latch. A clip is one block in four hundred, so a meter that only showed it while it was
/// happening would show it to nobody.
/// </summary>
public class ClipLatchTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AccumulatingStillAllocatesNothing()
    {
        MeterCells cells = new(4);

        // The latch is set from the audio thread, once per block per strip.
        AllocationAssert.None(cells, static target => target.Accumulate(0, 1.2f, 1.44, 120));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItLatchesAndStaysLatchedAcrossReads()
    {
        MeterCells cells = new(2);

        cells.Accumulate(0, 1.0f, 1.0, 120);
        cells.Take(0);

        // Taking the cell clears the peak, because the next frame needs a fresh one. It must not
        // clear the latch, or the indicator would live for exactly one meter frame - forty
        // milliseconds, which is nobody's reaction time.
        Assert.True(cells.HasClipped(0));

        cells.Take(0);
        cells.Take(0);

        Assert.True(cells.HasClipped(0));
        Assert.False(cells.HasClipped(1));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void JustBelowFullScaleIsNotAClip()
    {
        MeterCells cells = new(1);

        cells.Accumulate(0, 0.999f, 0.998, 120);

        Assert.False(cells.HasClipped(0));

        cells.Accumulate(0, MeterCells.FullScale, 1.0, 120);

        // At exactly one, not above it. That is the last moment the console can still warn, because
        // whatever converts this to an integer for a file or a device folds everything above it down
        // to the same value.
        Assert.True(cells.HasClipped(0));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OnlyAnOperatorClearsIt()
    {
        MeterCells cells = new(3);

        cells.Accumulate(0, 2.0f, 4.0, 120);
        cells.Accumulate(2, 2.0f, 4.0, 120);

        cells.ClearClip(0);

        Assert.False(cells.HasClipped(0));
        Assert.True(cells.HasClipped(2));

        cells.ClearClip(-1);

        Assert.False(cells.HasClipped(2));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ThePublisherCarriesTheLatchIntoTheFrame()
    {
        MeterCells channels = new(1);
        MeterCells buses = new(1);
        MeterPublisher publisher = new(channels, buses);

        channels.Accumulate(0, 1.5f, 2.25, 120);
        publisher.Publish(TimeSpan.FromMilliseconds(40), automix: null, depthDb: -15);

        Assert.True(publisher.Channels[0].HasClipped);

        publisher.ClearClip(0);
        publisher.Publish(TimeSpan.FromMilliseconds(40), automix: null, depthDb: -15);

        Assert.False(publisher.Channels[0].HasClipped);
    }
}
