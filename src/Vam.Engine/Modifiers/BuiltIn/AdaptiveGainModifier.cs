using Vam.Engine.Dsp;
using Vam.Engine.Dsp.Extensions;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// Brings a quiet talker up towards the same loudness as everybody else. B5.
/// </summary>
/// <remarks>
/// <para>
/// <b>Loudness, not level.</b> The measurement is EBU R128 short-term through the K-weighting
/// filters, because a level meter says a rumble and a voice at the same reading are equally loud
/// and a person listening does not agree. A council recording measured at around thirty decibels
/// below full scale is exactly the case this exists for.
/// </para>
/// <para>
/// <b>Deliberately slow.</b> Four seconds at minimum, which is far slower than a compressor and is
/// meant to be: this is correcting for where somebody is sitting, not for how they said a word. A
/// fast adaptive gain is indistinguishable from a compressor that is set wrongly, and it pumps.
/// </para>
/// <para>
/// The ceiling on how much it may add is the important safety limit. Without it, silence measures
/// as very quiet and the modifier answers by turning the room noise up to conversational level
/// during every pause.
/// </para>
/// </remarks>
public sealed class AdaptiveGainModifier : Modifier
{
    /// <summary>Ordinal of the loudness it aims for.</summary>
    public const int TargetOrdinal = 0;

    /// <summary>Ordinal of how much it may add.</summary>
    public const int MaximumGainOrdinal = 1;

    /// <summary>Ordinal of how slowly it moves.</summary>
    public const int ResponseOrdinal = 2;

    /// <summary>Ordinal of the loudness below which it stops trying.</summary>
    public const int GateOrdinal = 3;

    /// <summary>The window the standard measures short-term loudness over.</summary>
    public const double ShortTermSeconds = 3.0;

    const float MinimumLevelDb = -100f;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("target", "Target", "LUFS", -40f, -10f, -23f, ParameterCurve.Linear),
        new("maximum", "Maximum gain", "dB", 0f, 18f, 18f, ParameterCurve.Linear),
        new("response", "Response", "s", 4f, 30f, 8f, ParameterCurve.Linear),
        new("gate", "Silence gate", "LUFS", -70f, -30f, -50f, ParameterCurve.Linear)
    ];

    static readonly ModifierDescriptor Contract = new(
        "vam.adaptivegain",
        "Adaptive gain",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    KWeighting weighting = new(48000);
    float[] weighted = [];
    double[] window = [];
    int windowIndex;
    int windowFilled;
    double windowSum;
    int sampleRate = 48000;
    float gainDb;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <summary>The loudness it last measured, in units relative to full scale.</summary>
    public double ShortTermLoudness { get; private set; } = MinimumLevelDb;

    /// <summary>How much it is currently adding.</summary>
    public float AppliedGainDb => gainDb;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;

        weighting = new KWeighting(sampleRate);
        weighted = new float[Math.Max(maxFrames, 1)];

        // One slot per block, covering the standard's three-second window. Measuring per block
        // rather than per sample is the difference between a running sum and a transcendental
        // function on the audio thread.
        int blocks = Math.Max((int)(ShortTermSeconds * sampleRate / Math.Max(maxFrames, 1)), 1);

        window = new double[blocks];
        Reset();
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        Measure(ref context);

        float target = context.Parameters[TargetOrdinal];
        float maximum = context.Parameters[MaximumGainOrdinal];
        float response = context.Parameters[ResponseOrdinal];

        // Below the gate, hold whatever gain was last decided rather than chasing silence. A pause
        // measures as very quiet, and answering that by turning the room up is the failure mode
        // every automatic gain control is remembered for.
        if (ShortTermLoudness > context.Parameters[GateOrdinal] && windowFilled == window.Length)
        {
            float wanted = Math.Clamp((float)(target - ShortTermLoudness), 0f, maximum);
            float coefficient = (float)(1.0 - Math.Exp(-(double)context.FrameCount / (response * sampleRate)));

            gainDb += (wanted - gainDb) * coefficient;
        }

        float gain = gainDb <= 0f ? 1f : (float)Math.Pow(10.0, gainDb / 20.0);

        for (int channel = 0; channel < context.ChannelCount; channel++)
        {
            context.Channel(channel).Scale(gain);
        }

        context.Telemetry.LevelDb = (float)ShortTermLoudness;
        context.Telemetry.GainReductionDb = gainDb;
        context.Telemetry.IsActive = gainDb > 0.1f;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        weighting.Reset();

        Array.Clear(window);

        windowIndex = 0;
        windowFilled = 0;
        windowSum = 0.0;
        gainDb = 0f;
        ShortTermLoudness = MinimumLevelDb;
    }

    /// <summary>
    /// Adds this block to the three-second window and works out where loudness now sits.
    /// </summary>
    /// <remarks>
    /// A running sum over a ring of per-block mean squares. Removing the slot being overwritten and
    /// adding the new one keeps the whole measurement to two additions and one logarithm per block,
    /// however long the window is.
    /// </remarks>
    void Measure(ref ModifierContext context)
    {
        double meanSquare = 0.0;

        for (int channel = 0; channel < context.ChannelCount; channel++)
        {
            // Weighted on a copy. The measurement must not change what anybody hears.
            context.Channel(channel).CopyTo(weighted);

            Span<float> block = weighted.AsSpan(0, context.FrameCount);

            weighting.Process(block);
            meanSquare += ((ReadOnlySpan<float>)block).MeanSquare();
        }

        windowSum -= window[windowIndex];
        window[windowIndex] = meanSquare;
        windowSum += meanSquare;

        windowIndex = (windowIndex + 1) % window.Length;

        if (windowFilled < window.Length)
        {
            windowFilled++;
        }

        ShortTermLoudness = KWeighting.ToLoudness(windowSum / Math.Max(windowFilled, 1));
    }
}
