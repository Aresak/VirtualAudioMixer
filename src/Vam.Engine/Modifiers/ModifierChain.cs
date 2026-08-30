using System.Diagnostics;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers;

/// <summary>
/// An ordered set of modifiers over one strip or one bus, and everything they need to run.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>state</b>, not snapshot. The modifier instances, their filter histories, their
/// smoothed parameters and their cost cells all live here and are allocated when the chain is
/// built. What the operator can change lives in <see cref="ChainParams"/> instead — which is why
/// reordering a chain reuses the same instances rather than restarting every filter in it.
/// </para>
/// <para>
/// <b>The host smooths, not the modifier.</b> Once per block, in one place, so no modifier carries
/// parameter interpolation and no third-party one can get it wrong.
/// </para>
/// </remarks>
public sealed class ModifierChain
{
    /// <summary>What a block of nothing reports, rather than negative infinity.</summary>
    const float SilenceDb = -120f;

    readonly Modifier[] modifiers;
    readonly int[] parameterOffsets;
    readonly float[] smoothed;
    readonly ModifierTelemetry[] telemetry;
    readonly float[] outputLevels;
    readonly ModifierCost[] costs;
    readonly float[] scratch;
    readonly int channelCount;

    readonly string[] linkIds;

    /// <summary>Prepares a chain and everything it will allocate.</summary>
    /// <param name="links">The links, in order, head to tail, each with its identity.</param>
    /// <param name="channelCount">Channels the chain carries.</param>
    /// <param name="sampleRate">The rate audio arrives at.</param>
    /// <param name="maxFrames">Largest block the chain will be handed.</param>
    public ModifierChain(IReadOnlyList<ChainLink> links, int channelCount, int sampleRate, int maxFrames)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);

        modifiers = [.. links.Select(link => link.Modifier)];
        linkIds = [.. links.Select(link => link.LinkId)];
        this.channelCount = channelCount;

        parameterOffsets = new int[modifiers.Length + 1];
        telemetry = new ModifierTelemetry[modifiers.Length];
        outputLevels = new float[modifiers.Length];
        costs = new ModifierCost[modifiers.Length];

        int total = 0;

        for (int link = 0; link < modifiers.Length; link++)
        {
            parameterOffsets[link] = total;
            total += modifiers[link].Parameters.Length;

            // Every allocation a modifier will ever make happens here, on the control thread.
            modifiers[link].Prepare(sampleRate, maxFrames, channelCount);
        }

        parameterOffsets[^1] = total;
        smoothed = new float[total];
        scratch = new float[maxFrames * channelCount];

        SnapToDefaults();
    }

    /// <summary>Links in the chain.</summary>
    public int Count => modifiers.Length;

    /// <summary>Parameters across the whole chain.</summary>
    public int ParameterCount => smoothed.Length;

    /// <summary>
    /// Total delay the chain introduces, in samples.
    /// </summary>
    /// <remarks>
    /// The automixer needs this. It compares channels against each other, and without alignment it
    /// hands gain to whichever one happens to be ten milliseconds ahead because it has no denoise.
    /// </remarks>
    public int LatencySamples
    {
        get
        {
            int total = 0;

            foreach (Modifier modifier in modifiers)
            {
                total += modifier.Descriptor.LatencySamples;
            }

            return total;
        }
    }

    /// <summary>What each link is doing, for the meters.</summary>
    public ReadOnlySpan<ModifierTelemetry> Telemetry => telemetry;

    /// <summary>
    /// What is leaving each link, in dBFS, measured here rather than reported by the modifier.
    /// </summary>
    /// <remarks>
    /// A modifier's own <c>LevelDb</c> is whatever it found useful to publish — the compressor's
    /// detector, the adaptive gain's loudness — and several publish nothing at all. The level
    /// leaving a link is a property of the chain, so the chain measures it, and a modifier that
    /// wants to say something different about itself still can.
    /// </remarks>
    public ReadOnlySpan<float> OutputLevelsDb => outputLevels;

    /// <summary>What each link is costing, for K6 and the budget guard.</summary>
    public ReadOnlySpan<ModifierCost> Costs => costs;

    /// <summary>The links, in order.</summary>
    public ReadOnlySpan<Modifier> Modifiers => modifiers;

    /// <summary>Each link's identity, in the same order.</summary>
    public ReadOnlySpan<string> LinkIds => linkIds;

    /// <summary>
    /// Finds a link by identity, so a rebuild can keep the instance rather than starting it again.
    /// </summary>
    /// <param name="linkId">The identity.</param>
    /// <returns>The instance, or null when this chain does not hold it.</returns>
    public Modifier? Find(string linkId)
    {
        for (int link = 0; link < linkIds.Length; link++)
        {
            if (string.Equals(linkIds[link], linkId, StringComparison.Ordinal))
            {
                return modifiers[link];
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a proposed chain before anything is built from it.
    /// </summary>
    /// <remarks>
    /// Control thread, and the only place this is ever checked. Discovering a channel-count
    /// mismatch inside a callback would mean reporting it from the one thread that cannot report
    /// anything.
    /// </remarks>
    /// <param name="modifiers">The links, in the order proposed.</param>
    /// <param name="channelCount">Channels the chain will carry.</param>
    /// <returns>What is wrong. Empty means it can be built.</returns>
    public static IReadOnlyList<ChainProblem> Validate(IReadOnlyList<Modifier> modifiers, int channelCount)
    {
        ArgumentNullException.ThrowIfNull(modifiers);


        List<ChainProblem> problems = [];

        if (modifiers.Count > ChainParams.MaximumLinks)
        {
            problems.Add(new ChainProblem(
                ChainProblemKind.TooManyLinks,
                ChainParams.MaximumLinks,
                $"A chain may hold {ChainParams.MaximumLinks} links and this one has {modifiers.Count}."));
        }

        int width = channelCount;

        for (int link = 0; link < modifiers.Count; link++)
        {
            ModifierDescriptor descriptor = modifiers[link].Descriptor;

            if (!descriptor.Accepts(width))
            {
                problems.Add(new ChainProblem(
                    ChainProblemKind.ChannelCountMismatch,
                    link,
                    $"{descriptor.Name} takes {descriptor.ChannelsIn} channels and the link before it produces {width}."));

                return problems;
            }

            width = descriptor.ChannelsOutFor(width);
        }

        return problems;
    }

    /// <summary>
    /// Runs every link that is not bypassed. Audio thread.
    /// </summary>
    /// <param name="audio">The channels, processed in place.</param>
    /// <param name="parameters">What the operator has set. Slid towards, never jumped to.</param>
    /// <param name="frameCount">Frames in the block.</param>
    /// <param name="stride">Distance between the start of one channel and the next.</param>
    /// <param name="smoothing">How far a parameter travels towards its target this block.</param>
    public void Process(Span<float> audio, ChainParams parameters, int frameCount, int stride, float smoothing)
    {
        for (int link = 0; link < modifiers.Length; link++)
        {
            if (parameters.IsBypassed(link))
            {
                continue;
            }

            int from = parameterOffsets[link];
            int count = parameterOffsets[link + 1] - from;

            Smooth(modifiers[link], from, count, parameters.Targets, smoothing);

            long started = Stopwatch.GetTimestamp();

            ModifierContext context = new(
                audio,
                smoothed.AsSpan(from, count),
                scratch.AsSpan(0, frameCount),
                ref telemetry[link],
                channelCount,
                frameCount,
                stride);

            modifiers[link].Process(ref context);

            costs[link].Record(Stopwatch.GetTimestamp() - started);
            outputLevels[link] = PeakDb(audio, frameCount * channelCount);
        }
    }

    /// <summary>The block's peak, in dBFS. Inside the audio path.</summary>
    /// <remarks>
    /// One pass, no branches worth naming, and it answers the only question the console has about a
    /// link that appears to be doing nothing: is anything coming out of it.
    /// </remarks>
    static float PeakDb(ReadOnlySpan<float> audio, int samples)
    {
        float peak = 0f;

        for (int index = 0; index < samples && index < audio.Length; index++)
        {
            float magnitude = Math.Abs(audio[index]);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak <= 0f ? SilenceDb : (float)(20.0 * Math.Log10(peak));
    }

    /// <summary>Throws away every filter history and cost measurement. Control thread.</summary>
    public void Reset()
    {
        foreach (Modifier modifier in modifiers)
        {
            modifier.Reset();
        }

        for (int link = 0; link < costs.Length; link++)
        {
            costs[link].Clear();
        }

        Array.Clear(telemetry);
        SnapToDefaults();
    }

    void Smooth(Modifier modifier, int from, int count, ReadOnlySpan<float> targets, float smoothing)
    {
        ReadOnlySpan<ParameterDescriptor> descriptors = modifier.Parameters;

        for (int index = 0; index < count; index++)
        {
            ParameterDescriptor descriptor = descriptors[index];
            int ordinal = from + index;
            float target = ordinal < targets.Length ? descriptor.Clamp(targets[ordinal]) : descriptor.Default;

            switch (descriptor.Curve)
            {
                // Snapped. Sliding a twelve decibel slope through to a twenty-four decibel one would
                // pass through eighteen, which is not a setting the filter has.
                case ParameterCurve.Stepped:
                    smoothed[ordinal] = target;
                    break;

                // Smoothed as a gain, not as decibels, and the difference is not academic. A one-pole
                // run on decibels moves a tenth of the way each block, and a tenth of sixty decibels
                // is six - which is a six decibel step in amplitude at the first block boundary, and a
                // step in amplitude is the click all of this exists to avoid. Smoothed in the linear
                // domain the same one-pole moves the gain by a tenth, which is under a decibel.
                case ParameterCurve.Decibel:
                    SmoothDecibels(ordinal, target, descriptor.Minimum, smoothing);
                    break;

                default:
                    smoothed[ordinal] += (target - smoothed[ordinal]) * smoothing;
                    break;
            }
        }
    }

    void SmoothDecibels(int ordinal, float targetDb, float floorDb, float smoothing)
    {
        float targetGain = ToGain(targetDb);
        float currentGain = ToGain(smoothed[ordinal]);

        currentGain += (targetGain - currentGain) * smoothing;

        smoothed[ordinal] = currentGain <= ToGain(floorDb)
            ? floorDb
            : Math.Max(floorDb, (float)(20.0 * Math.Log10(currentGain)));
    }

    static float ToGain(float decibels) => (float)Math.Pow(10.0, decibels / 20.0);

    void SnapToDefaults()
    {
        for (int link = 0; link < modifiers.Length; link++)
        {
            ReadOnlySpan<ParameterDescriptor> descriptors = modifiers[link].Parameters;

            for (int index = 0; index < descriptors.Length; index++)
            {
                smoothed[parameterOffsets[link] + index] = descriptors[index].Default;
            }
        }
    }
}
