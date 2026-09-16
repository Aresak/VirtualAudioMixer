using Vam.Engine.Devices;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-010. The wrap tests are the ones that matter: an off-by-one at the wrap point is the
/// classic bug in this structure, and it surfaces after hours, which is precisely when nobody is
/// watching.
/// </summary>
public class AudioRingBufferTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void CapacityRoundsUpToAPowerOfTwo()
    {
        AudioRingBuffer ring = new(100, 1);

        Assert.Equal(128, ring.CapacityFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AContinuousRampSurvivesTheWrapPointUnbroken()
    {
        // Eight frames written five at a time: the second round straddles the wrap and every
        // round after it lands at a different offset, so this covers every split there is.
        AudioRingBuffer ring = new(8, 1);

        float[] writeBuffer = new float[5];
        float[] readBuffer = new float[5];
        float nextToWrite = 0;
        float nextExpected = 0;

        for (int round = 0; round < 200; round++)
        {
            for (int index = 0; index < writeBuffer.Length; index++)
            {
                writeBuffer[index] = nextToWrite++;
            }

            Assert.True(ring.TryWrite(writeBuffer));
            Assert.Equal(readBuffer.Length, ring.Read(readBuffer));

            for (int index = 0; index < readBuffer.Length; index++)
            {
                Assert.Equal(nextExpected++, readBuffer[index]);
            }
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void FramesStayInterleavedAcrossTheWrapPoint()
    {
        // Two channels doubles every index calculation, so a wrap that is right for mono can still
        // be wrong here - it would swap left and right for one buffer and nothing else.
        AudioRingBuffer ring = new(4, 2);

        float[] writeBuffer = new float[3 * 2];
        float[] readBuffer = new float[3 * 2];
        float nextToWrite = 0;
        float nextExpected = 0;

        for (int round = 0; round < 50; round++)
        {
            for (int index = 0; index < writeBuffer.Length; index++)
            {
                writeBuffer[index] = nextToWrite++;
            }

            Assert.True(ring.TryWrite(writeBuffer));
            Assert.Equal(3, ring.Read(readBuffer));

            for (int index = 0; index < readBuffer.Length; index++)
            {
                Assert.Equal(nextExpected++, readBuffer[index]);
            }
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void FillFramesIsCorrectAcrossTheWrapPoint()
    {
        AudioRingBuffer ring = new(8, 1);
        float[] five = new float[5];
        float[] three = new float[3];

        Assert.Equal(0, ring.FillFrames);
        Assert.Equal(8, ring.FreeFrames);

        Assert.True(ring.TryWrite(five));
        Assert.Equal(5, ring.FillFrames);

        Assert.Equal(3, ring.Read(three));
        Assert.Equal(2, ring.FillFrames);

        // Writing five from an offset of five straddles the wrap.
        Assert.True(ring.TryWrite(five));
        Assert.Equal(7, ring.FillFrames);
        Assert.Equal(1, ring.FreeFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFullRingRefusesTheWriteWholeAndCountsIt()
    {
        AudioRingBuffer ring = new(4, 1);

        Assert.True(ring.TryWrite(new float[4]));
        Assert.Equal(0, ring.OverrunCount);

        Assert.False(ring.TryWrite(new float[1]));
        Assert.Equal(1, ring.OverrunCount);

        // Nothing was written, so the ring is unchanged. A partial write would tear a frame
        // across the gap and the consumer would have no way to know.
        Assert.Equal(4, ring.FillFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnEmptyRingReadsShortAndCountsIt()
    {
        AudioRingBuffer ring = new(8, 1);

        Assert.True(ring.TryWrite(new float[2]));

        float[] destination = new float[5];
        Assert.Equal(2, ring.Read(destination));
        Assert.Equal(1, ring.UnderrunCount);

        Assert.Equal(0, ring.Read(destination));
        Assert.Equal(2, ring.UnderrunCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ResetReturnsTheRingToEmpty()
    {
        AudioRingBuffer ring = new(8, 1);
        Assert.True(ring.TryWrite(new float[5]));

        ring.Reset();

        Assert.Equal(0, ring.FillFrames);
        Assert.Equal(8, ring.FreeFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WritingAllocatesNothing()
    {
        AudioRingBuffer ring = new(64, 2);
        float[] block = new float[16 * 2];

        AllocationAssert.None((ring, block), static state =>
        {
            // Drained each time so the measured path is the write, not the refusal.
            state.ring.TryWrite(state.block);
            state.ring.Reset();
        });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ReadingAllocatesNothing()
    {
        AudioRingBuffer ring = new(64, 2);
        float[] source = new float[16 * 2];
        float[] destination = new float[16 * 2];

        AllocationAssert.None((ring, source, destination), static state =>
        {
            state.ring.TryWrite(state.source);
            state.ring.Read(state.destination);
        });
    }
}
