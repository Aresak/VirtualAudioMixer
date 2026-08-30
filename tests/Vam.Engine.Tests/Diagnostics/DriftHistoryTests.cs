using Vam.Engine.Diagnostics;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// K2. Whether the fill is holding or walking, which is the question the strip cannot answer.
/// </summary>
public class DriftHistoryTests
{
    static readonly DateTimeOffset Epoch = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EverythingWrittenComesBackOldestFirst()
    {
        DriftHistory history = new(capacity: 16);

        for (int index = 0; index < 10; index++)
        {
            history.Record(Sample(index));
        }

        DriftSample[] taken = new DriftSample[16];
        int written = history.CopyTo(taken);

        Assert.Equal(10, written);

        for (int index = 0; index < written; index++)
        {
            Assert.Equal(index, taken[index].ChannelIndex);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFullRingKeepsTheRecentPastInOrder()
    {
        DriftHistory history = new(capacity: 8);

        for (int index = 0; index < 40; index++)
        {
            history.Record(Sample(index));
        }

        DriftSample[] taken = new DriftSample[8];
        int written = history.CopyTo(taken);

        // Wrapped, but not scrambled. A chart drawn from a ring that came back starting in the
        // middle would show a device leaping between two drifts it never had.
        Assert.Equal(8, written);
        Assert.Equal(32, taken[0].ChannelIndex);
        Assert.Equal(39, taken[^1].ChannelIndex);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASmallDestinationGetsTheNewestRatherThanTheOldest()
    {
        DriftHistory history = new(capacity: 64);

        for (int index = 0; index < 40; index++)
        {
            history.Record(Sample(index));
        }

        DriftSample[] taken = new DriftSample[5];
        int written = history.CopyTo(taken);

        // The last five minutes, not the first five. Somebody opening this panel is asking what is
        // happening now.
        Assert.Equal(5, written);
        Assert.Equal(35, taken[0].ChannelIndex);
        Assert.Equal(39, taken[^1].ChannelIndex);
    }

    static DriftSample Sample(int index) =>
        new(index, Epoch.AddSeconds(index), index * 0.5, 50.0, index * 0.25);
}
