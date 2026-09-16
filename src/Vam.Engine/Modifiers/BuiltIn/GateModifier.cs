using Vam.Engine.Dsp;
using Vam.Engine.Dsp.Extensions;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// Closes a microphone that nobody is speaking into. B2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hysteresis and hold, because chattering is worse than leaking.</b> A gate with one threshold
/// opens and closes on every breath and every consonant tail, and the result is a stuttering
/// microphone that draws far more attention than the room noise it was hiding. Two thresholds and a
/// hold time mean it opens on a word and stays open through the gaps inside it.
/// </para>
/// <para>
/// Detected on the mean square rather than the peak. A peak detector opens on a pen click; speech
/// has sustained energy and a click does not.
/// </para>
/// </remarks>
public sealed class GateModifier : Modifier
{
    /// <summary>Ordinal of the level that opens the gate.</summary>
    public const int ThresholdOrdinal = 0;

    /// <summary>Ordinal of how far below that it closes again.</summary>
    public const int HysteresisOrdinal = 1;

    /// <summary>Ordinal of how long it stays open after the level drops.</summary>
    public const int HoldOrdinal = 2;

    /// <summary>Ordinal of how far down a closed gate takes the signal.</summary>
    public const int DepthOrdinal = 3;

    const float MinimumLevelDb = -100f;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("threshold", "Threshold", "dB", -80f, 0f, -45f, ParameterCurve.Decibel),
        new("hysteresis", "Hysteresis", "dB", 1f, 20f, 6f, ParameterCurve.Linear),
        new("hold", "Hold", "ms", 10f, 2000f, 250f, ParameterCurve.Linear),
        new("depth", "Depth", "dB", -80f, 0f, -20f, ParameterCurve.Decibel)
    ];

    static readonly ModifierDescriptor Contract = new(
        "vam.gate",
        "Gate",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    EnvelopeFollower follower = new();
    int sampleRate = 48000;
    int holdRemaining;
    bool isOpen;
    float gain = 1f;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <summary>Whether the gate is currently letting audio through.</summary>
    public bool IsOpen => isOpen;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;

        follower = new EnvelopeFollower();

        // Five milliseconds up so a word is not clipped at its start, two hundred down so the
        // envelope does not fall between syllables and hand the hold logic a false close.
        follower.SetTimes(0.005, 0.200, sampleRate);
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        float levelDb = LevelOf(ref context);
        float threshold = context.Parameters[ThresholdOrdinal];
        float hysteresis = context.Parameters[HysteresisOrdinal];

        UpdateState(levelDb, threshold, threshold - hysteresis, context.Parameters[HoldOrdinal], context.FrameCount);

        float target = isOpen ? 1f : ToGain(context.Parameters[DepthOrdinal]);

        // Slid across the block rather than switched. A gate that steps its gain is a click at the
        // start of every sentence, which is the one place nobody will forgive it.
        ApplyRamp(ref context, target);

        context.Telemetry.LevelDb = levelDb;
        context.Telemetry.GainReductionDb = isOpen ? 0f : context.Parameters[DepthOrdinal];
        context.Telemetry.IsActive = !isOpen;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        follower.Reset();

        holdRemaining = 0;
        isOpen = false;
        gain = 1f;
    }

    static float ToGain(float decibels) => decibels <= MinimumLevelDb ? 0f : (float)Math.Pow(10.0, decibels / 20.0);

    float LevelOf(ref ModifierContext context)
    {
        float meanSquare = 0f;

        for (int channel = 0; channel < context.ChannelCount; channel++)
        {
            meanSquare += ((ReadOnlySpan<float>)context.Channel(channel)).MeanSquare();
        }

        float magnitude = follower.Follow((float)Math.Sqrt(meanSquare / Math.Max(context.ChannelCount, 1)));

        return magnitude <= 0f ? MinimumLevelDb : (float)(20.0 * Math.Log10(magnitude));
    }

    void UpdateState(float levelDb, float openDb, float closeDb, float holdMilliseconds, int frameCount)
    {
        if (levelDb >= openDb)
        {
            isOpen = true;
            holdRemaining = (int)(holdMilliseconds * 0.001f * sampleRate);

            return;
        }

        if (!isOpen)
        {
            return;
        }

        // Below the lower threshold is what starts the hold running down; between the two, the gate
        // simply stays as it is. That gap is the whole reason it does not chatter.
        if (levelDb < closeDb)
        {
            holdRemaining -= frameCount;

            if (holdRemaining <= 0)
            {
                isOpen = false;
                holdRemaining = 0;
            }
        }
    }

    void ApplyRamp(ref ModifierContext context, float target)
    {
        int frames = context.FrameCount;

        if (frames == 0)
        {
            return;
        }

        float step = (target - gain) / frames;

        for (int channel = 0; channel < context.ChannelCount; channel++)
        {
            Span<float> samples = context.Channel(channel);
            float running = gain;

            for (int frame = 0; frame < samples.Length; frame++)
            {
                samples[frame] *= running;
                running += step;
            }
        }

        gain = target;
    }
}
