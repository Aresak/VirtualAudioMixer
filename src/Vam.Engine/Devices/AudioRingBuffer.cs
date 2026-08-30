using System.Numerics;

namespace Vam.Engine.Devices;

/// <summary>
/// A lock-free ring buffer for one producer and one consumer.
/// </summary>
/// <remarks>
/// <para>
/// The seam between a device thread and the mix thread. The engine's whole timing rests on this
/// one structure: the device thread writes and the mix clock reads, and <b>neither may ever wait
/// for the other</b>. A full buffer is an overrun and an empty one is an underrun; both are
/// counted and neither blocks.
/// </para>
/// <para>
/// <b>Exactly one producer and exactly one consumer.</b> That is what makes plain volatile reads
/// and writes sufficient here, with no <c>Interlocked</c> on the hot path: each cursor has a single
/// writer, and release/acquire ordering on the two of them is enough to publish the samples.
/// <b>Do not "improve" this into a multi-producer queue.</b> With two producers the cursor
/// arithmetic is no longer safe and every claim in this comment stops being true.
/// </para>
/// <para>
/// Both sides are inside the audio path - see <c>docs/audio-path.md</c>. Nothing here allocates
/// after construction.
/// </para>
/// </remarks>
public sealed class AudioRingBuffer
{
    readonly float[] buffer;
    readonly int capacityFrames;
    readonly int channelCount;
    readonly long mask;

    // One writer each: the producer owns head and overruns, the consumer owns tail and underruns.
    // They sit on separate cache lines - see PaddedCursor.
    PaddedCursor head;
    PaddedCursor tail;
    long overrunCount;
    long underrunCount;

    /// <summary>
    /// Allocates the ring. The only allocation this class ever performs.
    /// </summary>
    /// <param name="capacityFrames">
    /// Wanted capacity in frames. Rounded up to a power of two so the index can be masked rather
    /// than divided.
    /// </param>
    /// <param name="channelCount">Channels per frame.</param>
    public AudioRingBuffer(int capacityFrames, int channelCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);

        this.capacityFrames = (int)BitOperations.RoundUpToPowerOf2((uint)capacityFrames);
        this.channelCount = channelCount;

        mask = this.capacityFrames - 1;
        buffer = new float[this.capacityFrames * channelCount];
    }

    /// <summary>Frames the ring can hold. May exceed what was asked for, having been rounded up.</summary>
    public int CapacityFrames => capacityFrames;

    /// <summary>Channels per frame.</summary>
    public int ChannelCount => channelCount;

    /// <summary>
    /// Frames currently waiting to be read.
    /// </summary>
    /// <remarks>
    /// Safe to read from a third thread and cheap enough to poll, which matters because the drift
    /// estimator does exactly that: how far this sits from its target is the error signal the servo
    /// corrects. Reading it disturbs neither side.
    /// </remarks>
    public int FillFrames
    {
        get
        {
            long writtenTo = Volatile.Read(ref head.Value);
            long readTo = Volatile.Read(ref tail.Value);

            return (int)(writtenTo - readTo);
        }
    }

    /// <summary>Frames of free space.</summary>
    public int FreeFrames => capacityFrames - FillFrames;

    /// <summary>Writes the producer could not fit. Monotonic.</summary>
    public long OverrunCount => Volatile.Read(ref overrunCount);

    /// <summary>Reads the consumer could not fill. Monotonic.</summary>
    public long UnderrunCount => Volatile.Read(ref underrunCount);

    /// <summary>
    /// Writes whole frames. Producer side.
    /// </summary>
    /// <param name="frames">
    /// Interleaved samples, a whole number of frames of <see cref="ChannelCount"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if everything was written. <c>false</c> on overrun, having written nothing and
    /// counted it - a partial write would tear a frame across a gap.
    /// </returns>
    public bool TryWrite(ReadOnlySpan<float> frames)
    {
        int frameCount = frames.Length / channelCount;

        if (frameCount == 0)
        {
            return true;
        }

        // The producer owns head, so it can read its own cursor without synchronisation. It must
        // acquire the consumer's.
        long writtenTo = head.Value;
        long readTo = Volatile.Read(ref tail.Value);

        if (capacityFrames - (int)(writtenTo - readTo) < frameCount)
        {
            overrunCount++;
            return false;
        }

        int offset = (int)(writtenTo & mask);
        int untilWrap = capacityFrames - offset;

        if (frameCount <= untilWrap)
        {
            frames.CopyTo(buffer.AsSpan(offset * channelCount));
        }
        else
        {
            int firstSamples = untilWrap * channelCount;
            frames[..firstSamples].CopyTo(buffer.AsSpan(offset * channelCount));
            frames[firstSamples..].CopyTo(buffer.AsSpan(0));
        }

        // Release: the samples above must be visible before the consumer can see the new cursor.
        Volatile.Write(ref head.Value, writtenTo + frameCount);
        return true;
    }

    /// <summary>
    /// Reads whole frames. Consumer side.
    /// </summary>
    /// <param name="destination">
    /// Buffer to fill, a whole number of frames of <see cref="ChannelCount"/>. Anything not
    /// available is left untouched - the caller decides whether that means silence.
    /// </param>
    /// <returns>Frames actually read, which may be fewer than asked for.</returns>
    /// <remarks>
    /// Over the fourteen-statement limit, deliberately. A ring read is one wrap-around copy: the
    /// available count, the split into two runs at the boundary, both copies and the cursor
    /// publish. Every one of those is part of the same indivisible step, and a helper between them
    /// is a helper called on the audio thread for no benefit an operator could hear.
    /// </remarks>
    public int Read(Span<float> destination)
    {
        int wanted = destination.Length / channelCount;

        if (wanted == 0)
        {
            return 0;
        }

        long readTo = tail.Value;
        long writtenTo = Volatile.Read(ref head.Value);

        int available = (int)(writtenTo - readTo);
        int frameCount = Math.Min(wanted, available);

        if (frameCount < wanted)
        {
            underrunCount++;
        }

        if (frameCount == 0)
        {
            return 0;
        }

        int offset = (int)(readTo & mask);
        int untilWrap = capacityFrames - offset;

        if (frameCount <= untilWrap)
        {
            buffer.AsSpan(offset * channelCount, frameCount * channelCount).CopyTo(destination);
        }
        else
        {
            int firstSamples = untilWrap * channelCount;
            buffer.AsSpan(offset * channelCount, firstSamples).CopyTo(destination);
            buffer.AsSpan(0, (frameCount * channelCount) - firstSamples).CopyTo(destination[firstSamples..]);
        }

        Volatile.Write(ref tail.Value, readTo + frameCount);
        return frameCount;
    }

    /// <summary>
    /// Discards everything buffered and returns the ring to empty.
    /// </summary>
    /// <remarks>
    /// <b>Not safe while both sides are running.</b> Call it only when the stream is stopped -
    /// which is exactly the case it exists for: a device that disappeared and came back has a ring
    /// full of samples from before it left, and playing those out would be a glitch with a
    /// timestamp from several seconds ago.
    /// </remarks>
    public void Reset()
    {
        Volatile.Write(ref head.Value, 0);
        Volatile.Write(ref tail.Value, 0);
    }
}
