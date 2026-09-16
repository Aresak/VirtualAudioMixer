namespace Vam.Engine.Dsp;

/// <summary>
/// Managed noise suppression by spectral subtraction, until RNNoise arrives.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the honest fallback, not the intended answer.</b> EPIC-05 specifies RNNoise, which is
/// a trained model and sounds considerably better on speech than any amount of subtraction. This
/// exists so the chain, the seam and everything above them can be built and heard now, and it is
/// labelled in the console so nobody mistakes it for the real thing.
/// </para>
/// <para>
/// The method is the standard one: overlap-add short-time Fourier transform, a per-bin noise floor
/// learned during the quiet parts, and a subtraction with a floor under it. The floor matters more
/// than the subtraction does — subtracting all the way to zero produces the warbling artefacts that
/// make cheap denoise recognisable, so what is left behind is a small fraction of the estimate
/// rather than nothing.
/// </para>
/// <para>
/// Everything is allocated at construction. Inside the audio path.
/// </para>
/// </remarks>
public sealed class SpectralSubtractionSuppressor : INoiseSuppressor
{
    /// <summary>Transform size. Twenty-one milliseconds at forty-eight kilohertz.</summary>
    public const int FrameSize = 1024;

    /// <summary>Overlap, half a frame, which is what a Hann window needs to add back to unity.</summary>
    public const int HopSize = FrameSize / 2;

    /// <summary>
    /// How much of the estimate is left behind rather than removed. Subtracting all the way to zero
    /// is what makes cheap denoise sound like it is underwater.
    /// </summary>
    const float SpectralFloor = 0.08f;

    /// <summary>How fast the noise estimate rises. Slow, so a sentence is not learned as noise.</summary>
    const float NoiseRise = 0.0005f;

    /// <summary>How fast it falls. Faster than it rises, so the estimate follows a room going quiet.</summary>
    const float NoiseFall = 0.02f;

    readonly RealFft fft = new(FrameSize);
    readonly float[] window = new float[FrameSize];
    readonly float[] input = new float[FrameSize];
    readonly float[] output = new float[FrameSize];
    readonly float[] real = new float[FrameSize];
    readonly float[] imaginary = new float[FrameSize];
    readonly float[] noise;
    readonly int binCount;

    int filled;

    /// <summary>Builds the suppressor and every buffer it will use.</summary>
    public SpectralSubtractionSuppressor()
    {
        binCount = fft.BinCount;
        noise = new float[binCount];

        for (int index = 0; index < FrameSize; index++)
        {
            // Hann. At fifty per cent overlap two of these sum to a constant, which is what makes
            // overlap-add reconstruct the signal rather than modulate it.
            window[index] = (float)(0.5 - (0.5 * Math.Cos(2.0 * Math.PI * index / FrameSize)));
        }
    }

    /// <inheritdoc />
    public string Name => "Spectral subtraction (managed)";

    /// <inheritdoc />
    public int LatencySamples => FrameSize;

    /// <inheritdoc />
    public void Process(Span<float> samples, float strength)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            input[filled] = samples[index];
            samples[index] = output[filled];

            filled++;

            if (filled == HopSize)
            {
                Advance(strength);
                filled = 0;
            }
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(input);
        Array.Clear(output);
        Array.Clear(noise);

        filled = 0;
    }

    /// <summary>Transforms one hop, subtracts the noise estimate, and overlaps the result back.</summary>
    void Advance(float strength)
    {
        // The frame is the previous hop and this one. Shifting rather than indexing keeps the
        // arithmetic below simple at the cost of one copy of half a frame.
        Array.Copy(input, 0, real, HopSize, HopSize);
        Array.Clear(imaginary);

        for (int index = 0; index < FrameSize; index++)
        {
            real[index] *= window[index];
        }

        fft.Transform(real, imaginary, isInverse: false);
        Subtract(strength);
        fft.Transform(real, imaginary, isInverse: true);

        // Overlap-add: the tail of the previous frame plus the head of this one.
        for (int index = 0; index < HopSize; index++)
        {
            output[index] = (real[index] * window[index]) + output[index + HopSize];
            output[index + HopSize] = real[index + HopSize] * window[index + HopSize];
        }

        Array.Copy(input, 0, real, 0, HopSize);
    }

    void Subtract(float strength)
    {
        for (int bin = 0; bin < binCount; bin++)
        {
            float magnitude = (float)Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));

            // Learned asymmetrically. Rising slowly means a held vowel is not mistaken for noise;
            // falling faster means the estimate follows a room that has actually gone quieter
            // rather than staying pinned to the loudest thing it ever heard.
            noise[bin] += magnitude > noise[bin]
                ? (magnitude - noise[bin]) * NoiseRise
                : (magnitude - noise[bin]) * NoiseFall;

            if (magnitude <= 0f)
            {
                continue;
            }

            float remaining = magnitude - (noise[bin] * strength);
            float gain = Math.Max(remaining, magnitude * SpectralFloor) / magnitude;

            real[bin] *= gain;
            imaginary[bin] *= gain;

            // The transform of a real signal is symmetric, so the upper half follows the lower.
            int mirror = FrameSize - bin;

            if (bin > 0 && mirror < FrameSize)
            {
                real[mirror] *= gain;
                imaginary[mirror] *= gain;
            }
        }
    }
}
