namespace Vam.Engine.Dsp;

/// <summary>
/// The two-stage pre-filter every loudness measurement in ITU-R BS.1770 runs through.
/// </summary>
/// <remarks>
/// <para>
/// A high shelf and a high-pass, in that order. Between them they approximate what a head does to
/// sound arriving at it, which is why loudness measured through them agrees with what a person says
/// is louder and a plain root-mean-square does not.
/// </para>
/// <para>
/// <b>The forty-eight kilohertz coefficients are the ones written in the standard.</b> They are used
/// verbatim at that rate, because a loudness figure that disagrees with every other meter in the
/// building is worse than no loudness figure. At any other rate the same two filters are designed
/// from their described shapes, which is close but is not the standard, and that is said here rather
/// than left to be discovered.
/// </para>
/// </remarks>
public sealed class KWeighting
{
    /// <summary>The rate the standard's own coefficients are written for.</summary>
    public const int StandardSampleRate = 48000;

    /// <summary>The offset BS.1770 applies when turning mean square into loudness units.</summary>
    public const double LoudnessOffsetDb = -0.691;

    readonly Biquad shelf = new();
    readonly Biquad highPass = new();

    /// <summary>Designs the pair for a sample rate.</summary>
    /// <param name="sampleRate">The rate audio will arrive at.</param>
    public KWeighting(int sampleRate)
    {
        if (sampleRate == StandardSampleRate)
        {
            shelf.SetCoefficients(new BiquadCoefficients(
                1.53512485958697f,
                -2.69169618940638f,
                1.19839281085285f,
                -1.69065929318241f,
                0.73248077421585f));

            highPass.SetCoefficients(new BiquadCoefficients(1f, -2f, 1f, -1.99004745483398f, 0.99007225036621f));

            IsStandard = true;

            return;
        }

        shelf.SetCoefficients(BiquadDesign.HighShelf(1681.0, 3.999, sampleRate));
        highPass.SetCoefficients(BiquadDesign.HighPass(38.0, 0.5, sampleRate));
    }

    /// <summary>Whether this is the standard's own filter rather than an approximation of it.</summary>
    public bool IsStandard { get; }

    /// <summary>Weights one buffer in place.</summary>
    /// <param name="buffer">The samples, which are consumed rather than heard - weight a copy.</param>
    public void Process(Span<float> buffer)
    {
        shelf.Process(buffer);
        highPass.Process(buffer);
    }

    /// <summary>Turns a weighted mean square into loudness units.</summary>
    /// <param name="meanSquare">The mean of the squares of weighted samples.</param>
    /// <returns>Loudness, in units relative to full scale.</returns>
    public static double ToLoudness(double meanSquare) =>
        meanSquare <= 0.0 ? -100.0 : LoudnessOffsetDb + (10.0 * Math.Log10(meanSquare));

    /// <summary>Forgets the filters' history.</summary>
    public void Reset()
    {
        shelf.Reset();
        highPass.Reset();
    }
}
