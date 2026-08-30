namespace Vam.Engine.Dsp;

/// <summary>
/// A fixed delay, for anything that has to look at a signal before deciding what to do to it.
/// </summary>
/// <remarks>
/// <para>
/// The limiter's lookahead is the reason this exists: it delays the audio by as long as the
/// detector needs to see a peak coming, so the gain is already down by the time the peak arrives.
/// Without that a brick wall is not a brick wall, it is a very fast compressor that lets the first
/// millisecond through.
/// </para>
/// <para>
/// Also what aligns channels against each other before the automixer compares them — a strip with a
/// denoise in it is ten milliseconds behind one without, and gain sharing between them without
/// alignment hands the gain to whichever is early.
/// </para>
/// <para>
/// Inside the audio path. Allocated once; a power-of-two capacity so the index is masked.
/// </para>
/// </remarks>
public sealed class DelayLine
{
    readonly float[] buffer;
    readonly int mask;

    int writeIndex;
    int delaySamples;

    /// <summary>Allocates a line able to hold a given delay.</summary>
    /// <param name="maximumDelaySamples">Longest delay it will ever be asked for.</param>
    public DelayLine(int maximumDelaySamples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDelaySamples);

        int capacity = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(maximumDelaySamples + 1, 2));

        buffer = new float[capacity];
        mask = capacity - 1;
        delaySamples = maximumDelaySamples;
    }

    /// <summary>Longest delay this line can hold.</summary>
    public int Capacity => buffer.Length - 1;

    /// <summary>How far behind the output currently is.</summary>
    public int DelaySamples
    {
        get => delaySamples;
        set => delaySamples = Math.Clamp(value, 0, Capacity);
    }

    /// <summary>Pushes one sample in and takes one out.</summary>
    /// <param name="sample">What to put in.</param>
    /// <returns>What came out, from <see cref="DelaySamples"/> ago.</returns>
    public float Process(float sample)
    {
        buffer[writeIndex] = sample;

        int readIndex = (writeIndex - delaySamples) & mask;

        writeIndex = (writeIndex + 1) & mask;

        return buffer[readIndex];
    }

    /// <summary>Delays a whole buffer in place.</summary>
    /// <param name="samples">The samples.</param>
    public void Process(Span<float> samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = Process(samples[index]);
        }
    }

    /// <summary>Empties the line.</summary>
    public void Reset()
    {
        Array.Clear(buffer);
        writeIndex = 0;
    }
}
