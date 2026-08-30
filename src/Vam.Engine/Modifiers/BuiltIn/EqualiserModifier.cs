using Vam.Engine.Dsp;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// Four parametric bands and two shelves. B9.
/// </summary>
/// <remarks>
/// <para>
/// Four bands because a council chamber needs about three: something to take out the boxiness a
/// table microphone picks up around two hundred hertz, something for presence around three
/// kilohertz, and one spare for whatever that particular room does. The shelves are for the ends,
/// where a bell is the wrong shape.
/// </para>
/// <para>
/// Coefficients are redesigned only when a parameter has actually moved. The host smooths, so a
/// knob changes for about twenty milliseconds after somebody touches it and then stops — which puts
/// every sine and cosine in this modifier inside a gesture and none of them in a quiet session.
/// </para>
/// </remarks>
public sealed class EqualiserModifier : Modifier
{
    /// <summary>Parametric bands.</summary>
    public const int BandCount = 4;

    /// <summary>Parameters each band exposes: frequency, gain and Q.</summary>
    const int ParametersPerBand = 3;

    static readonly ParameterDescriptor[] ParameterDescriptors = BuildDescriptors();

    static readonly ModifierDescriptor Contract = new(
        "vam.equaliser",
        "Equaliser",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    Biquad[][] filters = [];
    float[] designed = [];
    int sampleRate = 48000;

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => Contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        this.sampleRate = sampleRate;

        // Bands plus the two shelves, one set of filters per channel.
        int sections = BandCount + 2;

        filters = new Biquad[channelCount][];

        for (int channel = 0; channel < channelCount; channel++)
        {
            filters[channel] = new Biquad[sections];

            for (int section = 0; section < sections; section++)
            {
                filters[channel][section] = new Biquad();
            }
        }

        designed = new float[ParameterDescriptors.Length];
        Array.Fill(designed, float.NaN);
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        Design(context.Parameters);

        int sections = BandCount + 2;

        for (int channel = 0; channel < context.ChannelCount && channel < filters.Length; channel++)
        {
            Span<float> samples = context.Channel(channel);

            for (int section = 0; section < sections; section++)
            {
                filters[channel][section].Process(samples);
            }
        }

        context.Telemetry.IsActive = true;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        foreach (Biquad[] channel in filters)
        {
            foreach (Biquad section in channel)
            {
                section.Reset();
            }
        }

        Array.Fill(designed, float.NaN);
    }

    static ParameterDescriptor[] BuildDescriptors()
    {
        List<ParameterDescriptor> descriptors = [];

        // Spread across the range a voice actually occupies rather than evenly across the spectrum.
        float[] defaults = [120f, 350f, 1600f, 4500f];

        for (int band = 0; band < BandCount; band++)
        {
            string prefix = $"band{band + 1}";

            descriptors.Add(new($"{prefix}.frequency", $"Band {band + 1} frequency", "Hz", 20f, 20000f, defaults[band], ParameterCurve.Logarithmic));
            descriptors.Add(new($"{prefix}.gain", $"Band {band + 1} gain", "dB", -18f, 18f, 0f, ParameterCurve.Linear));
            descriptors.Add(new($"{prefix}.q", $"Band {band + 1} Q", "", 0.2f, 8f, 1f, ParameterCurve.Logarithmic));
        }

        descriptors.Add(new("lowshelf.frequency", "Low shelf frequency", "Hz", 20f, 500f, 120f, ParameterCurve.Logarithmic));
        descriptors.Add(new("lowshelf.gain", "Low shelf gain", "dB", -18f, 18f, 0f, ParameterCurve.Linear));
        descriptors.Add(new("highshelf.frequency", "High shelf frequency", "Hz", 2000f, 20000f, 8000f, ParameterCurve.Logarithmic));
        descriptors.Add(new("highshelf.gain", "High shelf gain", "dB", -18f, 18f, 0f, ParameterCurve.Linear));

        return [.. descriptors];
    }

    void Design(ReadOnlySpan<float> parameters)
    {
        if (!HasMoved(parameters))
        {
            return;
        }

        parameters.CopyTo(designed);

        for (int band = 0; band < BandCount; band++)
        {
            int at = band * ParametersPerBand;

            Apply(band, BiquadDesign.Peaking(parameters[at], parameters[at + 2], parameters[at + 1], sampleRate));
        }

        int shelves = BandCount * ParametersPerBand;

        Apply(BandCount, BiquadDesign.LowShelf(parameters[shelves], parameters[shelves + 1], sampleRate));
        Apply(BandCount + 1, BiquadDesign.HighShelf(parameters[shelves + 2], parameters[shelves + 3], sampleRate));
    }

    bool HasMoved(ReadOnlySpan<float> parameters)
    {
        for (int index = 0; index < parameters.Length && index < designed.Length; index++)
        {
            if (parameters[index] != designed[index])
            {
                return true;
            }
        }

        return false;
    }

    void Apply(int section, BiquadCoefficients coefficients)
    {
        foreach (Biquad[] channel in filters)
        {
            channel[section].SetCoefficients(coefficients);
        }
    }
}
