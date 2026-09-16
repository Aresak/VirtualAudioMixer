namespace Vam.TestKit.Dsp;

/// <summary>
/// Measures the pitch of a tone in a captured buffer.
/// </summary>
/// <remarks>
/// <para>
/// A Goertzel at every bin of the loudest window rather than a fast transform. The question here is
/// a single-bin one - how much of frequency f is there, against how much of each neighbour - and
/// Goertzel answers it with two state variables and nothing to size or argue about. The cost is a
/// few hundred milliseconds once per test, which is free next to the two seconds of audio it is
/// looking at.
/// </para>
/// <para>
/// One channel at a time, de-interleaved by the stride the stream declared. That stride is as much
/// the assertion as the pitch is: a path told to expect one channel while two arrive reads a frame's
/// worth of samples per sample and comes back an octave down, which is the defect this exists to
/// catch.
/// </para>
/// <para>
/// Hann before the transform, then a parabola through the peak bin and its neighbours. A rectangular
/// window smears a tone that does not land on a bin centre across the whole spectrum, and 1 kHz
/// never lands on a bin centre.
/// </para>
/// </remarks>
/// <param name="sampleRate">The rate the buffer was captured at.</param>
/// <param name="channelCount">Channels interleaved in the buffer.</param>
public sealed class ToneAnalyser(int sampleRate, int channelCount)
{
    /// <summary>
    /// Frames measured. 8192 puts the bins 5.9 Hz apart at 48 kHz, which is a tenth of the 2 %
    /// tolerance before interpolation does anything at all.
    /// </summary>
    const int WindowFrames = 8192;

    /// <summary>How far to step while hunting for the loudest window.</summary>
    const int HopFrames = 1024;

    /// <summary>Below this there is no pitch to find, only rumble and any DC the device carries.</summary>
    const double LowestFrequencyHz = 20.0;

    readonly double[] window = new double[WindowFrames];

    /// <summary>Finds the dominant frequency in one channel of an interleaved capture.</summary>
    /// <param name="interleaved">The capture, as it came off the device.</param>
    /// <param name="channel">Which channel to measure.</param>
    /// <returns>What was found.</returns>
    public ToneMeasurement Measure(ReadOnlySpan<float> interleaved, int channel = 0)
    {
        int frameCount = interleaved.Length / channelCount;

        if (frameCount < WindowFrames)
        {
            throw new ArgumentException(
                $"Measuring needs {WindowFrames} frames and the capture has {frameCount}.",
                nameof(interleaved));
        }

        int start = LoudestWindowStart(interleaved, channel, frameCount);
        float peak = Fill(interleaved, channel, start);
        int bin = StrongestBin();

        return new ToneMeasurement(Refine(bin) * sampleRate / WindowFrames, peak, start);
    }

    /// <summary>
    /// The Goertzel recurrence for one bin, squared so no root is taken for a comparison.
    /// </summary>
    /// <param name="samples">The windowed block.</param>
    /// <param name="bin">Which bin, as a fraction of the block length.</param>
    /// <returns>Magnitude squared at that bin.</returns>
    static double MagnitudeSquared(ReadOnlySpan<double> samples, int bin)
    {
        double coefficient = 2.0 * Math.Cos(2.0 * Math.PI * bin / samples.Length);
        double previous = 0.0;
        double beforeThat = 0.0;

        for (int index = 0; index < samples.Length; index++)
        {
            double current = samples[index] + (coefficient * previous) - beforeThat;

            beforeThat = previous;
            previous = current;
        }

        return (previous * previous) + (beforeThat * beforeThat) - (coefficient * previous * beforeThat);
    }

    /// <summary>
    /// Where the strongest <see cref="WindowFrames"/> frames begin, so the silence a device plays
    /// while it primes is not what gets measured.
    /// </summary>
    int LoudestWindowStart(ReadOnlySpan<float> interleaved, int channel, int frameCount)
    {
        int best = 0;
        double bestEnergy = -1.0;

        for (int start = 0; start + WindowFrames <= frameCount; start += HopFrames)
        {
            double energy = 0.0;

            for (int frame = 0; frame < WindowFrames; frame++)
            {
                float sample = interleaved[((start + frame) * channelCount) + channel];
                energy += sample * sample;
            }

            if (energy > bestEnergy)
            {
                bestEnergy = energy;
                best = start;
            }
        }

        return best;
    }

    /// <summary>Copies one channel of a window out and applies the Hann taper.</summary>
    /// <param name="interleaved">The capture.</param>
    /// <param name="channel">Which channel.</param>
    /// <param name="start">First frame of the window.</param>
    /// <returns>The largest absolute sample in it, before the taper.</returns>
    float Fill(ReadOnlySpan<float> interleaved, int channel, int start)
    {
        float peak = 0f;

        for (int frame = 0; frame < WindowFrames; frame++)
        {
            float sample = interleaved[((start + frame) * channelCount) + channel];

            peak = Math.Max(peak, Math.Abs(sample));
            window[frame] = sample * (0.5 - (0.5 * Math.Cos(2.0 * Math.PI * frame / WindowFrames)));
        }

        return peak;
    }

    /// <summary>Scans every bin from <see cref="LowestFrequencyHz"/> up to Nyquist.</summary>
    /// <returns>The index of the strongest.</returns>
    int StrongestBin()
    {
        int lowest = Math.Max(1, (int)Math.Ceiling(LowestFrequencyHz * WindowFrames / sampleRate));
        int highest = (WindowFrames / 2) - 1;

        int best = lowest;
        double bestMagnitude = -1.0;

        for (int bin = lowest; bin <= highest; bin++)
        {
            double magnitude = MagnitudeSquared(window, bin);

            if (magnitude > bestMagnitude)
            {
                bestMagnitude = magnitude;
                best = bin;
            }
        }

        return best;
    }

    /// <summary>
    /// A parabola through the peak bin and its two neighbours, in decibels.
    /// </summary>
    /// <remarks>
    /// Log magnitudes rather than linear because a Hann-windowed peak is very nearly a parabola in
    /// decibels and not at all one in amplitude. Standard sinusoid peak interpolation.
    /// </remarks>
    /// <param name="bin">The strongest bin.</param>
    /// <returns>The peak's position in bins, fractional.</returns>
    double Refine(int bin)
    {
        if (bin <= 1 || bin >= (WindowFrames / 2) - 1)
        {
            return bin;
        }

        double before = Math.Log(MagnitudeSquared(window, bin - 1) + double.Epsilon);
        double at = Math.Log(MagnitudeSquared(window, bin) + double.Epsilon);
        double after = Math.Log(MagnitudeSquared(window, bin + 1) + double.Epsilon);

        double denominator = before - (2.0 * at) + after;

        return denominator == 0.0 ? bin : bin + (0.5 * (before - after) / denominator);
    }
}
