using Vam.Engine.Dsp;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// Evens out the difference between somebody leaning into a microphone and somebody sitting back. B6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Feed-forward and in the log domain.</b> Feed-forward because the gain is computed from the
/// input rather than from the output, which makes the ratio mean what it says instead of something
/// that depends on how hard it is working. Log domain because the knee and the ratio are both
/// defined in decibels, and computing them there avoids a pair of conversions per sample that would
/// otherwise be the most expensive thing in the modifier.
/// </para>
/// <para>
/// The gain reduction goes out as telemetry, which is what the strip's meter draws. That is not a
/// nicety: an operator needs to see that a compressor is working, because one that is doing nothing
/// and one that is doing far too much look identical from the level alone.
/// </para>
/// </remarks>
public sealed class CompressorModifier : Modifier
{
    /// <summary>Ordinal of the level above which it starts working.</summary>
    public const int ThresholdOrdinal = 0;

    /// <summary>Ordinal of how much it pushes back.</summary>
    public const int RatioOrdinal = 1;

    /// <summary>Ordinal of how quickly it reacts.</summary>
    public const int AttackOrdinal = 2;

    /// <summary>Ordinal of how quickly it lets go.</summary>
    public const int ReleaseOrdinal = 3;

    /// <summary>Ordinal of how wide the transition around the threshold is.</summary>
    public const int KneeOrdinal = 4;

    /// <summary>Ordinal of the level put back afterwards.</summary>
    public const int MakeUpOrdinal = 5;

    const float MinimumLevelDb = -100f;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("threshold", "Threshold", "dB", -60f, 0f, -24f, ParameterCurve.Decibel),
        new("ratio", "Ratio", ":1", 1f, 20f, 3f, ParameterCurve.Linear),
        new("attack", "Attack", "ms", 0.1f, 200f, 10f, ParameterCurve.Linear),
        new("release", "Release", "ms", 10f, 2000f, 200f, ParameterCurve.Linear),
        new("knee", "Knee", "dB", 0f, 24f, 6f, ParameterCurve.Linear),
        new("makeup", "Make-up", "dB", 0f, 24f, 0f, ParameterCurve.Decibel)
    ];

    static readonly ModifierDescriptor Contract = new(
        "vam.compressor",
        "Compressor",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    EnvelopeFollower follower = new();
    int sampleRate = 48000;
    float designedAttack = float.NaN;
    float designedRelease = float.NaN;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;
        follower = new EnvelopeFollower();
        designedAttack = float.NaN;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Over the fourteen-statement limit, deliberately. Detector, gain computer and make-up are one
    /// per-sample loop with shared state, and the published block diagram they implement is a single
    /// stage. Splitting it costs a call per sample and makes the code harder to check against the
    /// diagram, not easier.
    /// </remarks>
    public override void Process(ref ModifierContext context)
    {
        SetTimes(context.Parameters[AttackOrdinal], context.Parameters[ReleaseOrdinal]);

        float threshold = context.Parameters[ThresholdOrdinal];
        float ratio = Math.Max(context.Parameters[RatioOrdinal], 1f);
        float knee = context.Parameters[KneeOrdinal];
        float makeUp = ToGain(context.Parameters[MakeUpOrdinal]);
        float worstReduction = 0f;

        for (int frame = 0; frame < context.FrameCount; frame++)
        {
            // One detector across every channel, so a stereo pair keeps its image. Compressing the
            // sides independently moves a voice around between them, which is far more noticeable
            // than the level change either was correcting.
            float magnitude = 0f;

            for (int channel = 0; channel < context.ChannelCount; channel++)
            {
                magnitude = Math.Max(magnitude, Math.Abs(context.Channel(channel)[frame]));
            }

            float levelDb = ToDecibels(follower.Follow(magnitude));
            float reductionDb = ReductionFor(levelDb, threshold, ratio, knee);
            float gain = ToGain(reductionDb) * makeUp;

            worstReduction = Math.Min(worstReduction, reductionDb);

            for (int channel = 0; channel < context.ChannelCount; channel++)
            {
                context.Channel(channel)[frame] *= gain;
            }
        }

        context.Telemetry.GainReductionDb = worstReduction;
        context.Telemetry.LevelDb = ToDecibels(follower.Value);
        context.Telemetry.IsActive = worstReduction < -0.1f;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        follower.Reset();
        designedAttack = float.NaN;
    }

    /// <summary>
    /// How much gain to take away at a given level, in decibels. Negative means turning down.
    /// </summary>
    /// <remarks>
    /// The knee is a quadratic across a band centred on the threshold, which is the standard soft
    /// knee. A hard corner is audible on speech as a catch at the moment somebody gets louder; the
    /// curve makes the compressor start working before it is obviously working.
    /// </remarks>
    static float ReductionFor(float levelDb, float thresholdDb, float ratio, float kneeDb)
    {
        float over = levelDb - thresholdDb;

        if (kneeDb > 0f && over > -kneeDb / 2f && over < kneeDb / 2f)
        {
            float within = over + (kneeDb / 2f);

            return -(1f - (1f / ratio)) * within * within / (2f * kneeDb);
        }

        return over <= 0f ? 0f : -over * (1f - (1f / ratio));
    }

    static float ToGain(float decibels) => decibels <= MinimumLevelDb ? 0f : (float)Math.Pow(10.0, decibels / 20.0);

    static float ToDecibels(float gain) => gain <= 0f ? MinimumLevelDb : (float)(20.0 * Math.Log10(gain));

    void SetTimes(float attackMilliseconds, float releaseMilliseconds)
    {
        if (attackMilliseconds == designedAttack && releaseMilliseconds == designedRelease)
        {
            return;
        }

        designedAttack = attackMilliseconds;
        designedRelease = releaseMilliseconds;

        follower.SetTimes(attackMilliseconds * 0.001, releaseMilliseconds * 0.001, sampleRate);
    }
}
