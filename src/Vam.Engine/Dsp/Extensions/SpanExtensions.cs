using System.Numerics;

namespace Vam.Engine.Dsp.Extensions;

/// <summary>
/// The arithmetic every kernel in the engine needs, as extensions on the buffers themselves.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods rather than a static utility class, which is the carve-out the style rules
/// already grant — and it reads better inside a kernel: <c>buffer.FlushDenormals()</c> says what is
/// happening where a call to a helper class would not. Its own namespace, so <see cref="Span{T}"/>
/// does not sprout VAM methods everywhere in IntelliSense.
/// </para>
/// <para>
/// Inside the audio path.
/// </para>
/// </remarks>
public static class SpanExtensions
{
    /// <summary>
    /// Below this a sample is treated as zero.
    /// </summary>
    /// <remarks>
    /// <b>This is not tidiness, it is a deadline.</b> .NET cannot set flush-to-zero in the floating
    /// point control word from managed code, so denormal arithmetic runs at fifty to a hundred times
    /// the cost of normal arithmetic. With eleven filters per channel and a gate holding the signal
    /// near zero for minutes at a time, that lands the callback over its deadline in exactly the
    /// situation nobody would suspect: a quiet room.
    /// </remarks>
    public const float DenormalThreshold = 1e-20f;

    /// <summary>Zeroes anything too small to hear and too expensive to keep.</summary>
    /// <param name="buffer">The samples.</param>
    public static void FlushDenormals(this Span<float> buffer)
    {
        for (int index = 0; index < buffer.Length; index++)
        {
            if (Math.Abs(buffer[index]) < DenormalThreshold)
            {
                buffer[index] = 0f;
            }
        }
    }

    /// <summary>Adds one buffer into another at a gain.</summary>
    /// <param name="destination">Where to accumulate.</param>
    /// <param name="source">What to add.</param>
    /// <param name="gain">How much of it.</param>
    public static void MixInto(this Span<float> destination, ReadOnlySpan<float> source, float gain)
    {
        int count = Math.Min(destination.Length, source.Length);
        int vectorised = 0;

        if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            Vector<float> scale = new(gain);

            for (; vectorised <= count - Vector<float>.Count; vectorised += Vector<float>.Count)
            {
                Vector<float> accumulated = new Vector<float>(destination[vectorised..])
                    + (new Vector<float>(source[vectorised..]) * scale);

                accumulated.CopyTo(destination[vectorised..]);
            }
        }

        for (int index = vectorised; index < count; index++)
        {
            destination[index] += source[index] * gain;
        }
    }

    /// <summary>Scales a buffer in place.</summary>
    /// <param name="buffer">The samples.</param>
    /// <param name="gain">How much.</param>
    public static void Scale(this Span<float> buffer, float gain)
    {
        for (int index = 0; index < buffer.Length; index++)
        {
            buffer[index] *= gain;
        }
    }

    /// <summary>The largest absolute sample.</summary>
    /// <param name="buffer">The samples.</param>
    /// <returns>The peak, never negative.</returns>
    public static float PeakAbs(this ReadOnlySpan<float> buffer)
    {
        float peak = 0f;

        for (int index = 0; index < buffer.Length; index++)
        {
            float magnitude = Math.Abs(buffer[index]);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }

    /// <summary>The mean of the squares, which is what every level detector actually wants.</summary>
    /// <param name="buffer">The samples.</param>
    /// <returns>The mean square, or zero for an empty buffer.</returns>
    public static float MeanSquare(this ReadOnlySpan<float> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0f;
        }

        float sum = 0f;

        for (int index = 0; index < buffer.Length; index++)
        {
            sum += buffer[index] * buffer[index];
        }

        return sum / buffer.Length;
    }
}
