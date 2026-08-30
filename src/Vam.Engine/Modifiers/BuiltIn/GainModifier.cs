using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// A gain. The whole modifier framework, proved by the smallest thing that uses all of it.
/// </summary>
/// <remarks>
/// <para>
/// EPIC-04 ships one modifier on purpose, and it is this rather than a pure pass-through: a
/// pass-through would exercise the dispatch and nothing else, while a gain exercises a parameter,
/// its smoothing, its clamping and the telemetry a meter reads. If this works, the framework works.
/// </para>
/// <para>
/// The real modifiers are EPIC-05.
/// </para>
/// </remarks>
public sealed class GainModifier : Modifier
{
    /// <summary>Ordinal of the level parameter.</summary>
    public const int LevelOrdinal = 0;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("level", "Level", "dB", -60f, 12f, 0f, ParameterCurve.Decibel)
    ];

    static readonly ModifierDescriptor Contract = new(
        "vam.gain",
        "Gain",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <inheritdoc />
    /// <remarks>Nothing to allocate. A gain has no memory of anything.</remarks>
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        // Already smoothed by the host, so this is one multiply and no interpolation. That division
        // of labour is the point of the framework: a third-party author cannot get smoothing wrong
        // because they never do it.
        float decibels = context.Parameters[LevelOrdinal];
        float gain = decibels <= -60f ? 0f : (float)Math.Pow(10.0, decibels / 20.0);
        float peak = 0f;

        for (int channel = 0; channel < context.ChannelCount; channel++)
        {
            Span<float> samples = context.Channel(channel);

            for (int frame = 0; frame < samples.Length; frame++)
            {
                samples[frame] *= gain;
                peak = Math.Max(peak, Math.Abs(samples[frame]));
            }
        }

        context.Telemetry.LevelDb = peak <= 0f ? -100f : (float)(20.0 * Math.Log10(peak));
        context.Telemetry.GainReductionDb = Math.Min(decibels, 0f);
        context.Telemetry.IsActive = gain != 1f;
    }
}
