using Vam.Engine.Dsp;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers.BuiltIn;

/// <summary>
/// Takes the room out from behind the voices. B4.
/// </summary>
/// <remarks>
/// <para>
/// <b>What runs today is a managed spectral subtraction, and the console says so.</b> EPIC-05
/// specifies RNNoise through P/Invoke, which is a trained model and sounds considerably better. The
/// seam it will arrive through is <see cref="INoiseSuppressor"/>, and nothing above this modifier
/// knows which implementation is behind it — so swapping them is a registration change rather than
/// a rewrite. B4 stays open until the real one is in.
/// </para>
/// <para>
/// <b>The VAD tap is before this, not after.</b> A voice detector looking at denoised audio is
/// looking at audio that has already had the decision made for it, and it will agree with the
/// denoise rather than checking it.
/// </para>
/// </remarks>
public sealed class DenoiseModifier(Func<INoiseSuppressor>? factory = null) : Modifier
{
    /// <summary>Ordinal of how much noise to remove.</summary>
    public const int StrengthOrdinal = 0;

    static readonly ParameterDescriptor[] ParameterDescriptors =
    [
        new("strength", "Strength", "", 0f, 1f, 0.7f, ParameterCurve.Linear)
    ];

    INoiseSuppressor[] suppressors = [];
    ModifierDescriptor contract = new(
        "vam.denoise",
        "Denoise",
        ChannelsIn: 0,
        ChannelsOut: 0,
        LatencySamples: 0,
        CanProcessInPlace: true);

    /// <inheritdoc />
    public override ModifierDescriptor Descriptor => contract;

    /// <inheritdoc />
    public override ReadOnlySpan<ParameterDescriptor> Parameters => ParameterDescriptors;

    /// <summary>What is actually doing the work, for the console to show honestly.</summary>
    public string BackendName => suppressors.Length > 0 ? suppressors[0].Name : "none";

    /// <inheritdoc />
    public override void Prepare(int sampleRate, int maxFrames, int channelCount)
    {
        // One per channel. A shared suppressor would learn one noise estimate across every
        // microphone in the room, which is the opposite of what each of them needs.
        suppressors = new INoiseSuppressor[channelCount];

        for (int channel = 0; channel < channelCount; channel++)
        {
            suppressors[channel] = factory?.Invoke() ?? new SpectralSubtractionSuppressor();
        }

        // Declared rather than hidden. A strip with denoise on it is a frame behind one without, and
        // the automixer compares them against each other - unaligned, it hands the gain to whichever
        // is early rather than to whoever is speaking.
        contract = contract with { LatencySamples = suppressors[0].LatencySamples };
    }

    /// <inheritdoc />
    public override void Process(ref ModifierContext context)
    {
        float strength = context.Parameters[StrengthOrdinal];

        for (int channel = 0; channel < context.ChannelCount && channel < suppressors.Length; channel++)
        {
            suppressors[channel].Process(context.Channel(channel), strength);
        }

        context.Telemetry.IsActive = strength > 0.01f;
    }

    /// <inheritdoc />
    public override void Reset()
    {
        foreach (INoiseSuppressor suppressor in suppressors)
        {
            suppressor.Reset();
        }
    }
}
