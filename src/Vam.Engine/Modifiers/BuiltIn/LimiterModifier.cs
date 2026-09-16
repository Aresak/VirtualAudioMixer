using Vam.Engine.Dsp;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// A brick wall with lookahead, so nothing leaves the stream bus above the ceiling. D6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not optional on the stream bus.</b> Everything upstream can be set carefully and a councillor
/// can still bang the table, and what leaves the building has to survive that. A clipped peak on a
/// public broadcast is a permanent record of the moment nobody was watching the meters.
/// </para>
/// <para>
/// <b>Lookahead is what makes it a wall rather than a fast compressor.</b> The audio is delayed by
/// as long as the detector needs to see a peak coming, so the gain is already down when it arrives.
/// Without the delay the first millisecond of every transient goes through untouched, which is
/// precisely the part that would have clipped.
/// </para>
/// </remarks>
public sealed class LimiterModifier : Modifier
{
    /// <summary>Ordinal of the ceiling.</summary>
    public const int CeilingOrdinal = 0;

    /// <summary>Ordinal of how quickly it lets go.</summary>
    public const int ReleaseOrdinal = 1;

    /// <summary>
    /// How far ahead it looks. A millisecond and a half is enough for the gain to be down before a
    /// transient arrives, and short enough that the delay it adds is not worth thinking about.
    /// </summary>
    public const double LookaheadSeconds = 0.0015;

    const float MinimumLevelDb = -100f;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("ceiling", "Ceiling", "dB", -12f, 0f, -1f, ParameterCurve.Decibel),
        new("release", "Release", "ms", 10f, 1000f, 100f, ParameterCurve.Linear)
    ];

    static ModifierDescriptor contract = new(
        "vam.limiter",
        "Limiter",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    DelayLine[] delays = [];
    float[] lookaheadPeaks = [];
    int lookaheadSamples;
    int sampleRate = 48000;
    float gain = 1f;
    float releaseCoefficient = 1f;
    float designedRelease = float.NaN;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;

        lookaheadSamples = Math.Max((int)(LookaheadSeconds * sampleRate), 1);

        // Declared, so the automixer can align this bus against anything it is compared with. A
        // latency a modifier keeps to itself is a latency that shows up as a phase problem later.
        contract = contract with { LatencySamples = lookaheadSamples };

        delays = new DelayLine[channelCount];

        for (int channel = 0; channel < channelCount; channel++)
        {
            delays[channel] = new DelayLine(lookaheadSamples) { DelaySamples = lookaheadSamples };
        }

        lookaheadPeaks = new float[Math.Max(maxFrames, 1)];
        designedRelease = float.NaN;
        gain = 1f;
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        float ceiling = ToGain(context.Parameters[CeilingOrdinal]);

        SetRelease(context.Parameters[ReleaseOrdinal]);
        MeasureAhead(ref context);

        float worstReduction = 0f;

        for (int frame = 0; frame < context.FrameCount; frame++)
        {
            // The gain the loudest sample within the lookahead window demands. Attack is immediate
            // by construction - the peak has not been played yet - and only the release is smoothed.
            float required = lookaheadPeaks[frame] > ceiling ? ceiling / lookaheadPeaks[frame] : 1f;

            gain = required < gain ? required : gain + ((required - gain) * releaseCoefficient);

            for (int channel = 0; channel < context.ChannelCount && channel < delays.Length; channel++)
            {
                Span<float> samples = context.Channel(channel);

                samples[frame] = delays[channel].Process(samples[frame]) * gain;
            }

            worstReduction = Math.Min(worstReduction, ToDecibels(gain));
        }

        context.Telemetry.GainReductionDb = worstReduction;
        context.Telemetry.IsActive = worstReduction < -0.1f;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        foreach (DelayLine delay in delays)
        {
            delay.Reset();
        }

        gain = 1f;
        designedRelease = float.NaN;
    }

    static float ToGain(float decibels) => decibels <= MinimumLevelDb ? 0f : (float)Math.Pow(10.0, decibels / 20.0);

    static float ToDecibels(float gain) => gain <= 0f ? MinimumLevelDb : (float)(20.0 * Math.Log10(gain));

    /// <summary>
    /// Fills the lookahead window with the loudest sample each output frame will meet.
    /// </summary>
    /// <remarks>
    /// A sliding maximum, taken before anything is delayed. This is the "ahead" in lookahead: the
    /// peak at frame N of the delayed output is somewhere in the undelayed input around frame N,
    /// which is why the detector reads the input and the audio reads the delay line.
    /// </remarks>
    void MeasureAhead(ref ModifierContext context)
    {
        int frames = context.FrameCount;

        for (int frame = 0; frame < frames; frame++)
        {
            float peak = 0f;
            int until = Math.Min(frame + lookaheadSamples, frames - 1);

            for (int ahead = frame; ahead <= until; ahead++)
            {
                for (int channel = 0; channel < context.ChannelCount; channel++)
                {
                    peak = Math.Max(peak, Math.Abs(context.Channel(channel)[ahead]));
                }
            }

            lookaheadPeaks[frame] = peak;
        }
    }

    void SetRelease(float milliseconds)
    {
        if (milliseconds == designedRelease)
        {
            return;
        }

        designedRelease = milliseconds;
        releaseCoefficient = (float)(1.0 - Math.Exp(-1.0 / (milliseconds * 0.001 * sampleRate)));
    }
}
