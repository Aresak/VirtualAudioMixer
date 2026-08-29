namespace Vam.Engine.Devices.Clock;

/// <summary>
/// Corrects clock drift by resampling continuously at a ratio a hair away from 1.0.
/// </summary>
/// <remarks>
/// <para>
/// Drift is never corrected by dropping or duplicating samples. A dropped sample is a click, and a
/// click every few minutes for four hours is worse than the drift it was correcting.
/// </para>
/// <para>
/// <b>The riskiest single component in the project.</b> A resampler that is perfect on a sine wave
/// can add artefacts on speech that are subtle enough to ship, so its tests measure a sweep rather
/// than eyeball a waveform - and a listening check against real speech is still owed.
/// </para>
/// <para>
/// Inside the audio path. Everything is allocated when the resampler is constructed, and the ratio
/// is never branched on inside the inner loop.
/// </para>
/// </remarks>
public sealed class DriftResampler
{
    /// <summary>
    /// Filter length. Thirty-two taps of windowed sinc puts the stopband far below the noise floor
    /// of any conference microphone. It is affordable precisely because the ratio stays within a
    /// few hundred ppm of unity: a general-purpose rate converter would need more.
    /// </summary>
    public const int Taps = 32;

    /// <summary>
    /// Fractional delays in the table. Adjacent phases are interpolated between, so 512 is far
    /// more than the residual error needs - the table costs 64 KB once and nothing thereafter.
    /// </summary>
    public const int PhaseCount = 512;

    /// <summary>
    /// Kaiser window shape. 8.6 gives roughly -90 dB sidelobes, which is the point where the
    /// filter stops being what limits quality.
    /// </summary>
    const double KaiserBeta = 8.6;

    /// <summary>Widest ratio the servo is ever allowed to ask for. Beyond this something else is wrong.</summary>
    public const double MaxRatioDeviation = 0.0005;

    readonly float[] coefficients;
    readonly float[] work;
    readonly int channelCount;
    readonly int maxInputFrames;

    double position;
    double ratio = 1.0;

    /// <summary>
    /// Builds the polyphase table. The only allocation this class performs.
    /// </summary>
    /// <param name="channelCount">Channels per frame, resampled independently but in step.</param>
    /// <param name="maxInputFrames">Largest input block that will ever be passed to <see cref="Process"/>.</param>
    public DriftResampler(int channelCount, int maxInputFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInputFrames, 1);

        this.channelCount = channelCount;
        this.maxInputFrames = maxInputFrames;

        // One extra phase so the inner loop can interpolate towards phase + 1 without a bounds check.
        coefficients = new float[(PhaseCount + 1) * Taps];
        work = new float[(Taps + maxInputFrames) * channelCount];

        BuildTable(coefficients);
    }

    /// <summary>
    /// Input frames consumed per output frame. Slightly above 1.0 when the source device runs fast.
    /// </summary>
    /// <remarks>
    /// Settable while running. The fractional read position carries across calls, so changing it
    /// produces no discontinuity - which is what lets the servo nudge it continuously rather than
    /// in steps.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The ratio is further than <see cref="MaxRatioDeviation"/> from unity. That is not drift, and
    /// silently accepting it would let a broken estimate destroy the audio.
    /// </exception>
    public double Ratio
    {
        get => ratio;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1.0 - MaxRatioDeviation);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1.0 + MaxRatioDeviation);

            ratio = value;
        }
    }

    /// <summary>Channels per frame.</summary>
    public int ChannelCount => channelCount;

    /// <summary>
    /// Resamples one block.
    /// </summary>
    /// <param name="input">Interleaved input frames, at most the constructed maximum.</param>
    /// <param name="output">Where to write interleaved output frames.</param>
    /// <param name="consumed">Input frames used. The remainder should be offered again next call.</param>
    /// <param name="produced">Output frames written.</param>
    public void Process(ReadOnlySpan<float> input, Span<float> output, out int consumed, out int produced)
    {
        int inputFrames = input.Length / channelCount;
        int outputCapacity = output.Length / channelCount;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputFrames, maxInputFrames);

        // work = the retained tail of the previous block, then this block. Exactly Taps frames are
        // always retained, which is what makes `consumed` come out in input-frame coordinates.
        int historySamples = Taps * channelCount;
        input.CopyTo(work.AsSpan(historySamples));

        produced = 0;

        while (produced < outputCapacity && (int)position < inputFrames)
        {
            int frame = (int)position;
            double fraction = position - frame;

            double phasePosition = fraction * PhaseCount;
            int phase = (int)phasePosition;
            float phaseFraction = (float)(phasePosition - phase);

            int lowOffset = phase * Taps;
            int highOffset = lowOffset + Taps;
            int sourceOffset = frame * channelCount;
            int destinationOffset = produced * channelCount;

            for (int channel = 0; channel < channelCount; channel++)
            {
                float sum = 0f;

                for (int tap = 0; tap < Taps; tap++)
                {
                    float low = coefficients[lowOffset + tap];
                    float coefficient = low + (phaseFraction * (coefficients[highOffset + tap] - low));

                    sum += work[sourceOffset + (tap * channelCount) + channel] * coefficient;
                }

                output[destinationOffset + channel] = sum;
            }

            produced++;
            position += ratio;
        }

        consumed = Math.Min((int)position, inputFrames);

        // Retain the Taps frames the next block's filter window will reach back into, and keep the
        // fractional part of the position so the phase is continuous across the join.
        work.AsSpan(consumed * channelCount, historySamples).CopyTo(work);
        position -= consumed;
    }

    /// <summary>
    /// Clears the filter history and the read position.
    /// </summary>
    /// <remarks>
    /// For a device that disappeared and came back. Its old history is from before it left, and
    /// carrying it across would splice two unrelated moments together.
    /// </remarks>
    public void Reset()
    {
        Array.Clear(work);
        position = 0.0;
    }

    static void BuildTable(float[] table)
    {
        double normaliser = BesselI0(KaiserBeta);
        const double centre = (Taps / 2) - 1;
        const double halfLength = Taps / 2.0;

        for (int phase = 0; phase <= PhaseCount; phase++)
        {
            double delay = (double)phase / PhaseCount;
            double sum = 0.0;
            int offset = phase * Taps;

            for (int tap = 0; tap < Taps; tap++)
            {
                // Distance from this tap to where the filter's peak sits for this fractional
                // delay. The window is evaluated at the same coordinate, so the two stay centred
                // on each other - half a sample of mismatch here costs real stopband depth.
                double x = tap - centre - delay;

                // Cutoff sits at Nyquist. The ratio never moves far enough from unity for a guard
                // band to buy anything, and a lower cutoff would dull the top of the band instead.
                double sinc = x == 0.0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

                double windowPosition = Math.Clamp(x / halfLength, -1.0, 1.0);
                double window = BesselI0(KaiserBeta * Math.Sqrt(1.0 - (windowPosition * windowPosition))) / normaliser;

                double coefficient = sinc * window;
                table[offset + tap] = (float)coefficient;
                sum += coefficient;
            }

            // Unity gain at DC for every phase. Without this the output level would ripple in step
            // with the fractional position, which is an audible wobble rather than a rounding error.
            float scale = (float)(1.0 / sum);

            for (int tap = 0; tap < Taps; tap++)
            {
                table[offset + tap] *= scale;
            }
        }
    }

    static double BesselI0(double x)
    {
        double sum = 1.0;
        double term = 1.0;
        double half = x / 2.0;

        for (int index = 1; index < 64; index++)
        {
            term *= half / index;
            double contribution = term * term;
            sum += contribution;

            if (contribution < sum * 1e-16)
            {
                break;
            }
        }

        return sum;
    }
}
