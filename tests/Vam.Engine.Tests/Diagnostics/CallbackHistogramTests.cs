using Vam.Engine.Diagnostics;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// K4. The shape of the callback, not its average.
/// </summary>
public class CallbackHistogramTests
{
    const long BlockTicks = 1000;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RecordingFromTheAudioThreadAllocatesNothing()
    {
        CallbackHistogram histogram = new();

        // It is called once a block on the thread it is measuring. A histogram that allocated would
        // be the thing making the number it reports go up.
        AllocationAssert.None(histogram, static target => target.Record(400, BlockTicks));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SamplesLandInTheBucketTheyBelongIn()
    {
        CallbackHistogram histogram = new(bucketCount: 20, bucketWidthFraction: 0.1);

        histogram.Record(50, BlockTicks);
        histogram.Record(250, BlockTicks);
        histogram.Record(950, BlockTicks);

        long[] buckets = new long[histogram.BucketCount];

        histogram.CopyTo(buckets);

        Assert.Equal(1, buckets[0]);
        Assert.Equal(1, buckets[2]);
        Assert.Equal(1, buckets[9]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnythingPastTheEndLandsInTheLastBucketRatherThanNowhere()
    {
        CallbackHistogram histogram = new(bucketCount: 8, bucketWidthFraction: 0.1);

        histogram.Record(BlockTicks * 40, BlockTicks);

        long[] buckets = new long[histogram.BucketCount];

        histogram.CopyTo(buckets);

        // A callback forty times its budget is the single most interesting sample there is. Dropping
        // it because the chart does not go that far would lose exactly the one worth keeping.
        Assert.Equal(1, buckets[^1]);
        Assert.Equal(40.0, histogram.WorstFraction, 6);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OverrunsAreCountedAtTheBlockBoundary()
    {
        CallbackHistogram histogram = new();

        histogram.Record(BlockTicks - 1, BlockTicks);
        histogram.Record(BlockTicks, BlockTicks);
        histogram.Record(BlockTicks + 1, BlockTicks);

        // Exactly a block counts. At that point the callback has used everything it had, and the
        // next one starts late whether or not it is a single tick over.
        Assert.Equal(2, histogram.Overruns);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANonsenseBlockLengthIsIgnoredRatherThanDividedBy()
    {
        CallbackHistogram histogram = new();

        histogram.Record(500, 0);

        Assert.Equal(0, histogram.Overruns);
        Assert.Equal(0.0, histogram.WorstFraction);
    }
}
