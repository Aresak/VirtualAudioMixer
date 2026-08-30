namespace Vam.Engine.Dsp;

/// <summary>
/// Turns a frequency, a Q and a gain into biquad coefficients.
/// </summary>
/// <remarks>
/// <para>
/// The Audio EQ Cookbook formulas, unchanged. They are the standard because they are correct and
/// everybody can check them, and writing something original here would only make the filters harder
/// to verify.
/// </para>
/// <para>
/// One of the two plain static classes the style rules allow, because a coefficient factory has no
/// natural receiver — a frequency is not the thing being filtered.
/// </para>
/// <para>
/// <b>Control thread only.</b> Every method here computes sines and exponentials.
/// </para>
/// </remarks>
public static class BiquadDesign
{
    /// <summary>
    /// Highest Q the engine will design for.
    /// </summary>
    /// <remarks>
    /// Coefficients are ramped over a few dozen frames when a parameter moves, and linear
    /// interpolation between two coefficient sets is not unconditionally stable at high Q. Speech
    /// equalisation lives well below this, so the clamp costs nothing anybody would ask for and
    /// removes a class of failure that only appears while somebody is turning a knob.
    /// </remarks>
    public const double MaximumQ = 8.0;

    /// <summary>Lowest Q that still describes a filter rather than a very wide shelf.</summary>
    public const double MinimumQ = 0.1;

    /// <summary>A second-order high-pass. B1.</summary>
    /// <param name="frequencyHz">Where it turns over.</param>
    /// <param name="q">How sharply.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    /// <returns>The coefficients.</returns>
    public static BiquadCoefficients HighPass(double frequencyHz, double q, int sampleRate)
    {
        (double omega, double sin, double cos) = Angles(frequencyHz, sampleRate);
        double alpha = sin / (2.0 * Clamp(q));

        double a0 = 1.0 + alpha;
        double b0 = (1.0 + cos) / 2.0;

        return Normalise(b0, -(1.0 + cos), b0, -2.0 * cos, 1.0 - alpha, a0);
    }

    /// <summary>A second-order low-pass.</summary>
    /// <param name="frequencyHz">Where it turns over.</param>
    /// <param name="q">How sharply.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    /// <returns>The coefficients.</returns>
    public static BiquadCoefficients LowPass(double frequencyHz, double q, int sampleRate)
    {
        (double omega, double sin, double cos) = Angles(frequencyHz, sampleRate);
        double alpha = sin / (2.0 * Clamp(q));

        double a0 = 1.0 + alpha;
        double b0 = (1.0 - cos) / 2.0;

        return Normalise(b0, 1.0 - cos, b0, -2.0 * cos, 1.0 - alpha, a0);
    }

    /// <summary>A peaking bell. B9's four bands.</summary>
    /// <param name="frequencyHz">Its centre.</param>
    /// <param name="q">Its width.</param>
    /// <param name="gainDb">How much it lifts or cuts.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    /// <returns>The coefficients.</returns>
    public static BiquadCoefficients Peaking(double frequencyHz, double q, double gainDb, int sampleRate)
    {
        (double omega, double sin, double cos) = Angles(frequencyHz, sampleRate);
        double amplitude = Math.Pow(10.0, gainDb / 40.0);
        double alpha = sin / (2.0 * Clamp(q));

        double a0 = 1.0 + (alpha / amplitude);

        return Normalise(
            1.0 + (alpha * amplitude),
            -2.0 * cos,
            1.0 - (alpha * amplitude),
            -2.0 * cos,
            1.0 - (alpha / amplitude),
            a0);
    }

    /// <summary>A low shelf. B9.</summary>
    /// <param name="frequencyHz">Its corner.</param>
    /// <param name="gainDb">How much it lifts or cuts below it.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    /// <returns>The coefficients.</returns>
    public static BiquadCoefficients LowShelf(double frequencyHz, double gainDb, int sampleRate)
    {
        (double omega, double sin, double cos) = Angles(frequencyHz, sampleRate);
        double amplitude = Math.Pow(10.0, gainDb / 40.0);
        double beta = Math.Sqrt(amplitude) * sin / Math.Sqrt(2.0);

        double plus = amplitude + 1.0;
        double minus = amplitude - 1.0;
        double a0 = plus + (minus * cos) + beta;

        return Normalise(
            amplitude * (plus - (minus * cos) + beta),
            2.0 * amplitude * (minus - (plus * cos)),
            amplitude * (plus - (minus * cos) - beta),
            -2.0 * (minus + (plus * cos)),
            plus + (minus * cos) - beta,
            a0);
    }

    /// <summary>A high shelf. B9.</summary>
    /// <param name="frequencyHz">Its corner.</param>
    /// <param name="gainDb">How much it lifts or cuts above it.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    /// <returns>The coefficients.</returns>
    public static BiquadCoefficients HighShelf(double frequencyHz, double gainDb, int sampleRate)
    {
        (double omega, double sin, double cos) = Angles(frequencyHz, sampleRate);
        double amplitude = Math.Pow(10.0, gainDb / 40.0);
        double beta = Math.Sqrt(amplitude) * sin / Math.Sqrt(2.0);

        double plus = amplitude + 1.0;
        double minus = amplitude - 1.0;
        double a0 = plus - (minus * cos) + beta;

        return Normalise(
            amplitude * (plus + (minus * cos) + beta),
            -2.0 * amplitude * (minus + (plus * cos)),
            amplitude * (plus + (minus * cos) - beta),
            2.0 * (minus - (plus * cos)),
            plus - (minus * cos) - beta,
            a0);
    }

    static (double Omega, double Sin, double Cos) Angles(double frequencyHz, int sampleRate)
    {
        // Kept below Nyquist with a margin. A corner frequency at or above it is not a filter, and
        // the coefficients come out as something that oscillates rather than something that filters.
        double bounded = Math.Clamp(frequencyHz, 1.0, sampleRate * 0.49);
        double omega = 2.0 * Math.PI * bounded / sampleRate;

        return (omega, Math.Sin(omega), Math.Cos(omega));
    }

    static double Clamp(double q) => Math.Clamp(q, MinimumQ, MaximumQ);

    static BiquadCoefficients Normalise(double b0, double b1, double b2, double a1, double a2, double a0) =>
        new((float)(b0 / a0), (float)(b1 / a0), (float)(b2 / a0), (float)(a1 / a0), (float)(a2 / a0));
}
