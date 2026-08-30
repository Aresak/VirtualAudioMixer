using Vam.Engine.Dsp.Extensions;

namespace Vam.Engine.Dsp;

/// <summary>
/// Decides whether somebody is speaking into a microphone. B3.
/// </summary>
/// <remarks>
/// <para>
/// Three signals, because no one of them is enough on its own. <b>Band-limited energy</b> in the
/// range speech occupies, so a projector fan below it and a hiss above it do not count.
/// <b>Zero-crossing rate</b>, because a rustle and a hum have the same energy as a vowel and
/// completely different crossing rates. And an <b>adaptive noise floor</b>, so the threshold is
/// relative to whatever this particular room is doing rather than to a number chosen in advance.
/// </para>
/// <para>
/// The noise floor is what makes it work in a real chamber. A fixed threshold has to be set for the
/// quietest room it will ever meet and then leaks in every louder one, and nobody re-tunes it during
/// a meeting.
/// </para>
/// <para>
/// Read before the denoise. A detector looking at denoised audio agrees with the denoise rather than
/// checking it.
/// </para>
/// </remarks>
public sealed class VoiceActivityDetector
{
    /// <summary>Below this a sound is the building rather than a person.</summary>
    public const double LowFrequencyHz = 150.0;

    /// <summary>Above this a sound is a hiss or a rustle rather than a vowel.</summary>
    public const double HighFrequencyHz = 4000.0;

    /// <summary>How far above the noise floor speech has to be.</summary>
    const double MarginDb = 9.0;

    /// <summary>How fast the floor rises. Slow, so a sentence is not learned as background.</summary>
    const float FloorRise = 0.0008f;

    /// <summary>How fast it falls. Faster, so it follows a room that has gone quiet.</summary>
    const float FloorFall = 0.02f;

    /// <summary>Crossings per second above which the sound is too busy to be a voice.</summary>
    const double MaximumCrossingRate = 6000.0;

    /// <summary>Crossings per second below which it is a rumble rather than speech.</summary>
    const double MinimumCrossingRate = 200.0;

    readonly Biquad lowCut = new();
    readonly Biquad highCut = new();
    readonly float[] band;
    readonly int sampleRate;

    float noiseFloor;
    int holdRemaining;

    /// <summary>Builds a detector for one channel.</summary>
    /// <param name="sampleRate">The rate audio arrives at.</param>
    /// <param name="maxFrames">Largest block it will be handed.</param>
    public VoiceActivityDetector(int sampleRate, int maxFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);

        this.sampleRate = sampleRate;

        band = new float[maxFrames];

        lowCut.SetCoefficients(BiquadDesign.HighPass(LowFrequencyHz, 0.7071, sampleRate));
        highCut.SetCoefficients(BiquadDesign.LowPass(HighFrequencyHz, 0.7071, sampleRate));
    }

    /// <summary>Whether somebody is speaking, as of the last block.</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>The level in the speech band, in decibels relative to full scale.</summary>
    public float LevelDb { get; private set; } = -100f;

    /// <summary>What the detector currently believes the room's own noise is.</summary>
    public float NoiseFloorDb { get; private set; } = -100f;

    /// <summary>
    /// Looks at one block. Does not change it.
    /// </summary>
    /// <param name="samples">The block, read and left alone.</param>
    /// <returns>Whether somebody is speaking.</returns>
    public bool Observe(ReadOnlySpan<float> samples)
    {
        int frames = Math.Min(samples.Length, band.Length);

        if (frames == 0)
        {
            return IsSpeaking;
        }

        Span<float> filtered = band.AsSpan(0, frames);

        samples[..frames].CopyTo(filtered);

        lowCut.Process(filtered);
        highCut.Process(filtered);

        float magnitude = (float)Math.Sqrt(((ReadOnlySpan<float>)filtered).MeanSquare());

        LevelDb = magnitude <= 0f ? -100f : (float)(20.0 * Math.Log10(magnitude));

        UpdateFloor(magnitude);
        Decide(filtered, frames);

        return IsSpeaking;
    }

    /// <summary>Forgets the floor and the filters.</summary>
    public void Reset()
    {
        lowCut.Reset();
        highCut.Reset();

        noiseFloor = 0f;
        holdRemaining = 0;
        IsSpeaking = false;
        LevelDb = -100f;
        NoiseFloorDb = -100f;
    }

    static double CrossingRate(ReadOnlySpan<float> samples, int sampleRate)
    {
        int crossings = 0;

        for (int index = 1; index < samples.Length; index++)
        {
            if ((samples[index] >= 0f) != (samples[index - 1] >= 0f))
            {
                crossings++;
            }
        }

        return samples.Length <= 1 ? 0.0 : (double)crossings * sampleRate / samples.Length;
    }

    void UpdateFloor(float magnitude)
    {
        // Only learned while nobody is speaking. Learning during speech is how a detector talks
        // itself out of noticing the person who has been talking the longest.
        float coefficient = IsSpeaking ? 0f : magnitude > noiseFloor ? FloorRise : FloorFall;

        noiseFloor += (magnitude - noiseFloor) * coefficient;
        NoiseFloorDb = noiseFloor <= 0f ? -100f : (float)(20.0 * Math.Log10(noiseFloor));
    }

    void Decide(ReadOnlySpan<float> filtered, int frames)
    {
        double rate = CrossingRate(filtered, sampleRate);
        bool loudEnough = LevelDb > NoiseFloorDb + MarginDb;
        bool soundsLikeSpeech = rate is > MinimumCrossingRate and < MaximumCrossingRate;

        if (loudEnough && soundsLikeSpeech)
        {
            IsSpeaking = true;

            // Held across the gaps inside a sentence. Without it the detector flickers on every
            // stop consonant, and anything downstream flickers with it.
            holdRemaining = sampleRate / 4;

            return;
        }

        holdRemaining -= frames;

        if (holdRemaining <= 0)
        {
            IsSpeaking = false;
            holdRemaining = 0;
        }
    }
}
