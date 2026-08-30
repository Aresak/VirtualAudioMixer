using Vam.Engine.Dsp.Extensions;

namespace Vam.Engine.Dsp;

/// <summary>
/// One second-order filter section, in transposed direct form II.
/// </summary>
/// <remarks>
/// <para>
/// Transposed direct form II because it has the best numerical behaviour of the four arrangements
/// at the frequencies this engine cares about. A high-pass at eighty hertz running at forty-eight
/// kilohertz has its poles very close to the unit circle, and direct form I loses precision there
/// in a way that shows up as noise rather than as anything obviously wrong.
/// </para>
/// <para>
/// <b>Coefficients are computed on the control thread and set here.</b> Nothing in this class
/// converts a frequency into a coefficient — that is a handful of transcendental functions and it
/// belongs where there is time for it.
/// </para>
/// <para>
/// Inside the audio path. One instance per channel, because the state is the channel's.
/// </para>
/// </remarks>
public sealed class Biquad
{
    float b0 = 1f;
    float b1;
    float b2;
    float a1;
    float a2;
    float z1;
    float z2;

    /// <summary>Sets the coefficients, already normalised so that a0 is one.</summary>
    /// <param name="coefficients">What to filter with.</param>
    public void SetCoefficients(BiquadCoefficients coefficients)
    {
        b0 = coefficients.B0;
        b1 = coefficients.B1;
        b2 = coefficients.B2;
        a1 = coefficients.A1;
        a2 = coefficients.A2;
    }

    /// <summary>Filters one buffer in place.</summary>
    /// <param name="buffer">The samples.</param>
    public void Process(Span<float> buffer)
    {
        // The published transposed direct form II recurrence. The symbols keep their standard names
        // because renaming them makes the code harder to check against the source, not easier.
        for (int sampleIndex = 0; sampleIndex < buffer.Length; sampleIndex++)
        {
            float x = buffer[sampleIndex];
            float y = (b0 * x) + z1;

            z1 = (b1 * x) - (a1 * y) + z2;
            z2 = (b2 * x) - (a2 * y);

            buffer[sampleIndex] = y;
        }

        // The feedback path is where denormals accumulate, and a gate holding a channel near silence
        // for minutes is exactly how they get there.
        if (Math.Abs(z1) < SpanExtensions.DenormalThreshold)
        {
            z1 = 0f;
        }

        if (Math.Abs(z2) < SpanExtensions.DenormalThreshold)
        {
            z2 = 0f;
        }
    }

    /// <summary>Forgets the filter's history.</summary>
    public void Reset()
    {
        z1 = 0f;
        z2 = 0f;
    }
}
