using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph.Extensions;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Modifiers;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Graph;

/// <summary>
/// Turns a console's configuration into a plan and the first snapshot to render with.
/// </summary>
/// <remarks>
/// <para>
/// Everything expensive happens here, on the control thread: allocating the arena, working out the
/// plane layout, building the nodes, converting decibels to gains, and deciding which sends
/// mix-minus forbids. The audio thread inherits a flat array and a block of floats.
/// </para>
/// <para>
/// <b>Mix-minus is computed, not configured.</b> An operator cannot switch it on, cannot switch it
/// off, and cannot forget it — it falls out of the declared endpoint pairs every time the graph is
/// compiled. That is deliberate: the failure it prevents is the most embarrassing one this project
/// has, a councillor hearing themselves late in front of a public gallery, and "a send that
/// defaults to off" would be one careless click away from it.
/// </para>
/// </remarks>
public sealed class GraphCompiler(int blockFrames, int sampleRate, ModifierRegistry? registry = null)
{
    /// <summary>
    /// How long a parameter takes to travel most of the way to a new value. Twenty milliseconds is
    /// short enough to feel immediate under an operator's hand and long enough that the largest
    /// jump the console can make - a send switched on at full level - arrives as a swell rather
    /// than a click.
    /// </summary>
    const double SmoothingTimeSeconds = 0.020;

    /// <summary>
    /// Compiles a configuration.
    /// </summary>
    /// <param name="config">What the operator set up.</param>
    /// <param name="busOutputs">
    /// Index-aligned with the buses. A non-null entry sends that bus to a device other than the one
    /// keeping time; null means the bus either feeds the primary output or nothing at all.
    /// </param>
    /// <returns>A snapshot ready to publish, over a freshly allocated plan.</returns>
    public GraphSnapshot Compile(
        GraphConfig config,
        IReadOnlyList<BusOutputChannel?>? busOutputs = null,
        GraphPlan? previous = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        GraphLayout layout = BuildLayout(config);
        RenderArena arena = new(layout.PlaneCount, blockFrames);

        arena.Clear();

        GraphPlan plan = new(arena, BuildNodes(config, layout, SmoothingCoefficient(), busOutputs, previous));

        return new GraphSnapshot(
            plan,
            BuildChannels(config),
            BuildBuses(config),
            BuildSends(config),
            BuildChains(config, plan));
    }

    /// <summary>
    /// Rebuilds the parameters over an existing plan.
    /// </summary>
    /// <remarks>
    /// For everything that changes a level, a flag or a routing without changing the shape of the
    /// console. The plan and its arena are reference-identical to the previous snapshot's, so this
    /// costs a few small arrays on the control thread and nothing at all on the audio thread — which
    /// is what lets a dragged fader publish repeatedly without the collector noticing.
    /// </remarks>
    /// <param name="config">What the operator set up.</param>
    /// <param name="previous">The snapshot whose plan to keep.</param>
    /// <returns>A new snapshot over the same plan.</returns>
    public static GraphSnapshot Rebuild(GraphConfig config, GraphSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(previous);

        return previous
            .WithSends(BuildSends(config))
            .WithChannels(BuildChannels(config))
            .WithBuses(BuildBuses(config))
            .WithChains(BuildChains(config, previous.Plan));
    }

    static GraphLayout BuildLayout(GraphConfig config)
    {
        List<int> channelWidths = [];
        List<int> busWidths = [];

        foreach (ChannelConfig channel in config.Channels)
        {
            // A folded strip is one channel wide however many the device offers, so the fold is
            // reflected in the layout rather than costing a plane that is never read.
            channelWidths.Add((channel.Flags & ChannelFlags.MonoFold) != 0 ? 1 : Math.Max(channel.ChannelCount, 1));
        }

        foreach (BusConfig bus in config.Buses)
        {
            busWidths.Add(Math.Max(bus.ChannelCount, 1));
        }

        return new GraphLayout(channelWidths, busWidths);
    }

    float SmoothingCoefficient()
    {
        // One block's worth of a one-pole. Advanced once per block, so the coefficient is fixed at
        // compile time and the kernels never see the arithmetic.
        double blockSeconds = (double)blockFrames / sampleRate;

        return (float)(1.0 - Math.Exp(-blockSeconds / SmoothingTimeSeconds));
    }

    AudioNode[] BuildNodes(
        GraphConfig config,
        GraphLayout layout,
        float smoothing,
        IReadOnlyList<BusOutputChannel?>? busOutputs,
        GraphPlan? previous)
    {
        List<AudioNode> nodes = [];
        List<ModifierChain> chains = BuildModifierChains(config, layout, previous);

        // The order is the topology. Inputs fill the pre-fader planes, faders fill the post-fader
        // planes from them, buses read both, and the output reads a bus - so a plain walk of this
        // array is a valid evaluation order by construction, and the audio thread never traverses
        // anything.
        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            nodes.Add(new InputNode(layout, channel, DeviceIndexOf(config, channel)));
        }

        // Between the head stage and the fader, and that position is the contract. Everything before
        // is the fixed head, everything after is the fixed tail, and the operator composes what
        // happens in between - so the anchors are places in the plan rather than a rule the console
        // is trusted to enforce.
        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            if (chains[channel].Count > 0)
            {
                nodes.Add(new ChainNode(layout, channel, chains[channel], smoothing));
            }
        }

        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            nodes.Add(new FaderNode(layout, channel, smoothing));
        }

        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            nodes.Add(new BusMixNode(layout, bus, config.Channels.Count, smoothing));
        }

        if (config.Buses.Count > 0)
        {
            int primary = Math.Clamp(config.PrimaryBusIndex, 0, config.Buses.Count - 1);

            nodes.Add(new PrimaryOutputNode(layout, primary, Math.Max(config.PrimaryOutputChannelCount, 1)));
        }

        AddSecondaryOutputs(config, layout, busOutputs, blockFrames, nodes);

        return [.. nodes];
    }

    static void AddSecondaryOutputs(
        GraphConfig config,
        GraphLayout layout,
        IReadOnlyList<BusOutputChannel?>? busOutputs,
        int blockFrames,
        List<AudioNode> nodes)
    {
        if (busOutputs is null)
        {
            return;
        }

        for (int bus = 0; bus < config.Buses.Count && bus < busOutputs.Count; bus++)
        {
            if (busOutputs[bus] is BusOutputChannel destination)
            {
                nodes.Add(new BusOutputNode(layout, bus, destination, blockFrames));
            }
        }
    }

    List<ModifierChain> BuildModifierChains(GraphConfig config, GraphLayout layout, GraphPlan? previous)
    {
        List<ModifierChain> chains = [];

        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            ModifierChain? existing = ChainFor(previous, channel);
            List<ChainLink> links = [];

            foreach (ModifierSetting setting in config.Channels[channel].Chain)
            {
                // Kept if we already have it. A reorder that minted fresh instances would restart
                // every filter history and envelope in the chain, and a denoise restarting
                // mid-sentence is audible - which is exactly what a reorder must not be.
                Modifier? modifier = existing?.Find(setting.LinkId) ?? registry?.Create(setting.ModifierId);

                // An identifier nobody registered is left out rather than throwing. A configuration
                // naming a third-party modifier that is not installed should open with a gap in the
                // chain, not refuse to open at all - a session has to start.
                if (modifier is not null)
                {
                    links.Add(new ChainLink(setting.LinkId, modifier));
                }
            }

            chains.Add(new ModifierChain(links, layout.ChannelWidth(channel), sampleRate, blockFrames));
        }

        return chains;
    }

    static ModifierChain? ChainFor(GraphPlan? plan, int channelIndex)
    {
        if (plan is null)
        {
            return null;
        }

        foreach (AudioNode node in plan.Nodes)
        {
            if (node is ChainNode chain && chain.ChannelIndex == channelIndex)
            {
                return chain.Chain;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves each strip's saved settings against the chain that is actually installed.
    /// </summary>
    /// <remarks>
    /// By parameter identifier, never by position. The audio thread reads by ordinal because that
    /// is fast; configuration reads by name because that is stable, and this is the one place the
    /// two meet.
    /// </remarks>
    static ChainParams[] BuildChains(GraphConfig config, GraphPlan plan)
    {
        ChainParams[] result = new ChainParams[config.Channels.Count];

        Array.Fill(result, ChainParams.Empty);

        foreach (AudioNode node in plan.Nodes)
        {
            if (node is ChainNode chain && chain.ChannelIndex < result.Length)
            {
                result[chain.ChannelIndex] = BuildChain(config.Channels[chain.ChannelIndex], chain.Chain);
            }
        }

        return result;
    }

    static ChainParams BuildChain(ChannelConfig channel, ModifierChain chain)
    {
        float[] targets = new float[chain.ParameterCount];
        ulong bypass = 0UL;
        int ordinal = 0;

        for (int link = 0; link < chain.Count; link++)
        {
            ModifierSetting? setting = link < channel.Chain.Count ? channel.Chain[link] : null;
            ReadOnlySpan<ParameterDescriptor> descriptors = chain.Modifiers[link].Parameters;

            if (setting is not null && setting.IsBypassed)
            {
                bypass |= 1UL << link;
            }

            for (int index = 0; index < descriptors.Length; index++)
            {
                ParameterDescriptor descriptor = descriptors[index];

                targets[ordinal++] = setting is not null && setting.Values.TryGetValue(descriptor.Id, out float saved)
                    ? descriptor.Clamp(saved)
                    : descriptor.Default;
            }
        }

        return new ChainParams(targets, bypass);
    }

    static int DeviceIndexOf(GraphConfig config, int channelIndex)
    {
        AudioDeviceId wanted = config.Channels[channelIndex].DeviceId;

        for (int index = 0; index < config.InputDeviceOrder.Count; index++)
        {
            if (config.InputDeviceOrder[index] == wanted)
            {
                return index;
            }
        }

        // Past the end of any block's device set, which InputNode reads as "not here" and answers
        // with silence. A strip configured for a device nobody has plugged in is quiet, not broken.
        return int.MaxValue;
    }

    static ChannelParams[] BuildChannels(GraphConfig config)
    {
        ChannelParams[] channels = new ChannelParams[config.Channels.Count];

        for (int index = 0; index < channels.Length; index++)
        {
            ChannelConfig channel = config.Channels[index];

            channels[index] = new ChannelParams(
                channel.TrimDb.ToLinearGain(),
                channel.FaderDb.ToLinearGain(),
                channel.Flags,
                (channel.Flags & ChannelFlags.MonoFold) != 0 ? 1 : Math.Max(channel.ChannelCount, 1));
        }

        return channels;
    }

    static BusParams[] BuildBuses(GraphConfig config)
    {
        BusParams[] buses = new BusParams[config.Buses.Count];

        for (int index = 0; index < buses.Length; index++)
        {
            BusConfig bus = config.Buses[index];

            buses[index] = new BusParams(
                bus.GainDb.ToLinearGain(),
                bus.Role,
                Math.Max(bus.ChannelCount, 1),
                bus.IsMuted);
        }

        return buses;
    }

    static SendMatrix BuildSends(GraphConfig config)
    {
        SendMatrix sends = new(config.Channels.Count, config.Buses.Count);

        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            for (int bus = 0; bus < config.Buses.Count; bus++)
            {
                bool excluded = IsExcludedByMixMinus(config, channel, bus);

                sends.Set(channel, bus, excluded ? SendState.ExcludedMixMinus : SendState.Off, 0f);
            }
        }

        // Applied after the exclusions and never over them. An operator can ask for this send; the
        // compiler is where the answer is no, so there is no path through the console, the protocol
        // or a restored configuration file that can turn a mix-minus exclusion back on.
        foreach (SendConfig send in config.Sends)
        {
            if (!IsInRange(config, send) || sends.StateOf(send.ChannelIndex, send.BusIndex) == SendState.ExcludedMixMinus)
            {
                continue;
            }

            sends.Set(
                send.ChannelIndex,
                send.BusIndex,
                send.IsOn ? SendState.On : SendState.Off,
                send.LevelDb.ToLinearGain());
        }

        return sends;
    }

    static bool IsInRange(GraphConfig config, SendConfig send)
    {
        return send.ChannelIndex >= 0
            && send.ChannelIndex < config.Channels.Count
            && send.BusIndex >= 0
            && send.BusIndex < config.Buses.Count;
    }

    /// <summary>
    /// Whether sending this strip to this bus would play somebody their own voice.
    /// </summary>
    static bool IsExcludedByMixMinus(GraphConfig config, int channelIndex, int busIndex)
    {
        AudioDeviceId source = config.Channels[channelIndex].DeviceId;
        AudioDeviceId destination = config.Buses[busIndex].OutputDeviceId;

        if (destination.IsNone)
        {
            return false;
        }

        foreach (EndpointPair pair in config.EndpointPairs)
        {
            if (pair.CaptureDeviceId == source && pair.RenderDeviceId == destination)
            {
                return true;
            }
        }

        return false;
    }
}
