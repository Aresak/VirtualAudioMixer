using Vam.Engine.Dsp;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// A high-pass, to take the room out from under the voices. B1.
/// </summary>
/// <remarks>
/// <para>
/// First in almost every chain, and for a council chamber it earns its place before anything else:
/// air conditioning, a projector fan and the building itself all live below a hundred hertz, and
/// none of it is speech. Removing it before the gate and the compressor means neither of them is
/// reacting to a rumble nobody can hear.
/// </para>
/// <para>
/// Twelve or twenty-four decibels per octave, as one or two cascaded sections. The slope is a
/// stepped parameter because eighteen is not a setting either arrangement has.
/// </para>
/// </remarks>
public sealed class HighPassModifier : Modifier
{
    /// <summary>Ordinal of the corner frequency.</summary>
    public const int FrequencyOrdinal = 0;

    /// <summary>Ordinal of the slope.</summary>
    public const int SlopeOrdinal = 1;

    /// <summary>Butterworth Q for a single second-order section.</summary>
    const double ButterworthQ = 0.70710678;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("frequency", "Frequency", "Hz", 20f, 400f, 80f, ParameterCurve.Logarithmic),
        new("slope", "Slope", "dB/oct", 12f, 24f, 12f, ParameterCurve.Stepped)
    ];

    static readonly ModifierDescriptor Contract = new(
        "vam.highpass",
        "High-pass",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    Biquad[][] sections = [];
    float designedFrequency = float.NaN;
    int designedSlope;
    int sampleRate = 48000;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;

        // Two sections per channel, allocated whether or not the steeper slope is selected. The
        // alternative is allocating when somebody turns a knob, which is an allocation on the
        // control thread that the audio thread then has to see appear underneath it.
        sections = new Biquad[channelCount][];

        for (int channel = 0; channel < channelCount; channel++)
        {
            sections[channel] = [new Biquad(), new Biquad()];
        }

        designedFrequency = float.NaN;
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        float frequency = context.Parameters[FrequencyOrdinal];
        int order = context.Parameters[SlopeOrdinal] >= 18f ? 2 : 1;

        Design(frequency, order);

        for (int channel = 0; channel < context.ChannelCount && channel < sections.Length; channel++)
        {
            Span<float> samples = context.Channel(channel);

            for (int section = 0; section < order; section++)
            {
                sections[channel][section].Process(samples);
            }
        }

        context.Telemetry.IsActive = true;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        foreach (Biquad[] channel in sections)
        {
            foreach (Biquad section in channel)
            {
                section.Reset();
            }
        }
    }

    void Design(float frequency, int order)
    {
        // Redesigned only when something moved. The host smooths, so a parameter changes for about
        // twenty milliseconds after an operator touches it and then stops - which means the sines
        // and cosines here happen during a gesture and never during a quiet session.
        if (frequency == designedFrequency && order == designedSlope)
        {
            return;
        }

        designedFrequency = frequency;
        designedSlope = order;

        // Two cascaded Butterworth sections give twenty-four decibels per octave with a flat
        // passband; two at the same Q would give a resonant bump at the corner instead.
        double first = order == 2 ? 0.54119610 : ButterworthQ;
        double second = 1.30656296;

        BiquadCoefficients firstSection = BiquadDesign.HighPass(frequency, first, sampleRate);
        BiquadCoefficients secondSection = BiquadDesign.HighPass(frequency, second, sampleRate);

        foreach (Biquad[] channel in sections)
        {
            channel[0].SetCoefficients(firstSection);
            channel[1].SetCoefficients(secondSection);
        }
    }
}
