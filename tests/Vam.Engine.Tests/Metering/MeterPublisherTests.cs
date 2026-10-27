using Vam.Engine.Metering;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Metering;

/// <summary>
/// What a console draws from the publisher's readings.
/// </summary>
public class MeterPublisherTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void BeforeItsFirstFrameEveryMeterReadsTheFloor()
    {
        MeterPublisher publisher = new(new MeterCells(3), new MeterCells(2));

        // Startup and every recompile show these before the first Publish. Full scale here is a
        // one-frame flash of every meter hitting the top.
        foreach (MeterReading reading in publisher.Channels.ToArray().Concat(publisher.Buses.ToArray()))
        {
            Assert.Equal(-120.0, reading.PeakDb);
            Assert.Equal(-120.0, reading.RmsDb);
        }

        Assert.Equal(3, publisher.Channels.Length);
        Assert.Equal(2, publisher.Buses.Length);
    }
}
