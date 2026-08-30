using System.Globalization;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Allocations;

/// <summary>
/// Covers the four things the gate has to get right: it catches boxing, it catches small
/// allocations, it does not fire on span arithmetic, and its closure-free overload measures
/// nothing for a body that does nothing.
/// </summary>
public class AllocationAssertTests
{
    const int SpanArithmeticRuns = 1000;

    // Written to from the measured bodies so the JIT cannot elide the allocations under test.
    static object? sink;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void BoxingAnIntFailsAndReportsTheByteCount()
    {
        AllocationAssertException failure = Assert.Throws<AllocationAssertException>(
            () => AllocationAssert.None(42, static value => sink = value));

        Assert.True(failure.Measurement.HasAllocated);
        Assert.Contains(
            failure.Measurement.Bytes.ToString(CultureInfo.InvariantCulture),
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASixteenByteArrayFails()
    {
        AllocationAssertException failure = Assert.Throws<AllocationAssertException>(
            () => AllocationAssert.None(16, static size => sink = new byte[size]));

        Assert.True(failure.Measurement.HasAllocated);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SpanArithmeticPassesAThousandConsecutiveRuns()
    {
        float[] buffer = new float[64];

        // Flakiness here is worse than absence: a gate that fails at random gets disabled
        // within a week, and then nothing is protecting anything.
        for (int run = 0; run < SpanArithmeticRuns; run++)
        {
            AllocationAssert.None(buffer, static samples =>
            {
                Span<float> block = samples.AsSpan();

                for (int index = 0; index < block.Length; index++)
                {
                    block[index] = (block[index] * 0.5f) + 0.25f;
                }
            });
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheClosureFreeOverloadMeasuresZeroForAnEmptyBody()
    {
        AllocationMeasurement measurement = AllocationAssert.Measure(0, static _ => { });

        Assert.Equal(0, measurement.Bytes);
        Assert.False(measurement.HasAllocated);
        Assert.Equal(-1, measurement.FirstOffendingIteration);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheFailureMessagePointsAtTheBoundaryDocument()
    {
        AllocationAssertException failure = Assert.Throws<AllocationAssertException>(
            () => AllocationAssert.None(1, static size => sink = new byte[size]));

        Assert.Contains("docs/audio-path.md", failure.Message, StringComparison.Ordinal);
        Assert.Contains("allocates, locks or waits", failure.Message, StringComparison.Ordinal);
    }
}
