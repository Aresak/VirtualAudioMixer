using Vam.Engine.Automix;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph.Extensions;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Metering;
using Vam.Engine.Modifiers;
using Vam.Engine.Recording;
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
        GraphPlan? previous = null,
        RecordingSession? recording = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        GraphLayout layout = BuildLayout(config);
        RenderArena arena = new(layout.PlaneCount, blockFrames);

        arena.Clear();

        GraphPlan plan = new(
            arena,
            BuildNodes(config, layout, SmoothingCoefficient(), busOutputs, previous, recording));

        return new GraphSnapshot(
            plan,
            BuildChannels(config),
            BuildBuses(config),
            BuildSends(config),
            BuildChains(config, plan),
            BuildAutomix(config));
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
            .WithChains(BuildChains(config, previous.Plan))
            .WithAutomix(BuildAutomix(config));
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
        GraphPlan? previous,
        RecordingSession? recording)
    {
        List<AudioNode> nodes = [];
        List<ModifierChain> chains = BuildModifierChains(config, layout, previous);
        List<ModifierChain> busChains = BuildBusChains(config, layout, previous);

        // The order is the topology. Inputs fill the pre-fader planes, faders fill the post-fader
        // planes from them, buses read both, and the output reads a bus - so a plain walk of this
        // array is a valid evaluation order by construction, and the audio thread never traverses
        // anything.
        for (int channel = 0; channel < config.Channels.Count; channel++)
        {
            nodes.Add(new InputNode(layout, channel, DeviceIndexOf(config, channel)));
        }

        // Straight after the head stage, before anything decided something was silence or noise. The
        // multitrack is the raw material a session gets rebuilt from, and it is only worth having if
        // the processing has not already happened to it.
        AddRecordingTaps(config, layout, recording, nodes);

        // B3. Before the chain, so the detector sees what the microphone sent rather than what the
        // denoise left of it. A detector reading denoised audio agrees with the denoise instead of
        // checking it.
        VoiceActivityTapNode voiceActivity = new(layout, config.Channels.Count, sampleRate, blockFrames);

        nodes.Add(voiceActivity);

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

        AddAlignAndAutomix(config, layout, chains, voiceActivity, nodes);
        AddBusStage(config, layout, busChains, smoothing, nodes);

        // E3's second half: the stream bus, finished, beside the raw inputs.
        AddBusRecordingTap(config, layout, recording, nodes);

        // After the buses are summed, so what a bus meter shows is what the bus is actually
        // carrying rather than what went into it.
        nodes.Add(new MeterNode(layout, new MeterCells(config.Channels.Count), new MeterCells(config.Buses.Count)));

        if (config.Buses.Count > 0)
        {
            int primary = Math.Clamp(config.PrimaryBusIndex, 0, config.Buses.Count - 1);

            nodes.Add(new PrimaryOutputNode(layout, primary, Math.Max(config.PrimaryOutputChannelCount, 1)));
        }

        AddSecondaryOutputs(config, layout, busOutputs, blockFrames, nodes);

        return [.. nodes];
    }

    /// <summary>
    /// Aligns the strips for the latency their chains added, then shares the gain between them.
    /// </summary>
    /// <remarks>
    /// Aligned, then shared, then mixed. The alignment has to be after the chains because that is
    /// where the latencies come from, and before the automixer because the automixer compares the
    /// strips against each other - unaligned it favours whichever finished first.
    /// </remarks>
    void AddAlignAndAutomix(
        GraphConfig config,
        GraphLayout layout,
        List<ModifierChain> chains,
        VoiceActivityTapNode voiceActivity,
        List<AudioNode> nodes)
    {
        List<int> latencies = [];

        foreach (ModifierChain chain in chains)
        {
            latencies.Add(chain.LatencySamples);
        }

        LatencyAlignNode align = new(layout, latencies);

        if (align.AlignedChannelCount > 0)
        {
            nodes.Add(align);
        }

        nodes.Add(new AutomixNode(layout, new AutomixState(config.Channels.Count), sampleRate, blockFrames)
        {
            // B3 feeding C1. The automixer weights its detector by how far each strip is above its
            // own noise floor, so a microphone next to the air conditioning does not win the gain
            // by being the loudest thing in the room.
            VoiceActivity = voiceActivity
        });
    }

    /// <summary>Sums each bus, then runs whatever chain that bus carries.</summary>
    /// <remarks>
    /// D6. After the sum, because limiting each microphone separately does not stop the sum of them
    /// clipping - and before the meter, because a bus meter has to show what the bus is sending.
    /// </remarks>
    static void AddBusStage(
        GraphConfig config,
        GraphLayout layout,
        List<ModifierChain> busChains,
        float smoothing,
        List<AudioNode> nodes)
    {
        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            nodes.Add(new BusMixNode(layout, bus, config.Channels.Count, smoothing));
        }

        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            if (busChains[bus].Count > 0)
            {
                nodes.Add(new BusChainNode(layout, bus, busChains[bus], smoothing));
            }
        }
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

    /// <summary>Builds one chain per bus, keeping the instances a previous plan already had.</summary>
    /// <param name="config">What the operator set up.</param>
    /// <param name="layout">Where the planes are.</param>
    /// <param name="previous">The plan being replaced, if any.</param>
    /// <returns>One chain per bus, in order.</returns>
    List<ModifierChain> BuildBusChains(GraphConfig config, GraphLayout layout, GraphPlan? previous)
    {
        List<ModifierChain> chains = [];

        for (int bus = 0; bus < config.Buses.Count; bus++)
        {
            ModifierChain? existing = BusChainFor(previous, bus);
            List<ChainLink> links = [];

            foreach (ModifierSetting setting in EffectiveBusChain(config.Buses[bus]))
            {
                Modifier? modifier = existing?.Find(setting.LinkId) ?? registry?.Create(setting.ModifierId);

                if (modifier is not null)
                {
                    links.Add(new ChainLink(setting.LinkId, modifier));
                }
            }

            chains.Add(new ModifierChain(links, layout.BusWidth(bus), sampleRate, blockFrames));
        }

        return chains;
    }

    static ModifierChain? BusChainFor(GraphPlan? plan, int busIndex)
    {
        if (plan is null)
        {
            return null;
        }

        foreach (AudioNode node in plan.Nodes)
        {
            if (node is BusChainNode chain && chain.BusIndex == busIndex)
            {
                return chain.Chain;
            }
        }

        return null;
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
    /// <summary>The limiter a stream bus gets whether or not anybody asked for it. D6.</summary>
    public const string MandatoryLimiterId = "vam.limiter";

    /// <summary>
    /// Its link identity, fixed rather than minted.
    /// </summary>
    /// <remarks>
    /// A fresh identity each compile would mean a fresh instance each compile, and the limiter's
    /// envelope restarting every time somebody moved a fader — audible, on the one bus that must
    /// never make a noise of its own.
    /// </remarks>
    public const string MandatoryLimiterLinkId = "vam-stream-limiter";

    static ChainParams[] BuildChains(GraphConfig config, GraphPlan plan)
    {
        ChainParams[] result = new ChainParams[config.Channels.Count + config.Buses.Count];

        Array.Fill(result, ChainParams.Empty);

        foreach (AudioNode node in plan.Nodes)
        {
            if (node is ChainNode chain && chain.ChannelIndex < config.Channels.Count)
            {
                result[chain.ChannelIndex] = BuildChain(config.Channels[chain.ChannelIndex].Chain, chain.Chain);
            }

            // Buses sit after the strips in the same array. See GraphSnapshot.BusChainOf.
            if (node is BusChainNode bus && bus.BusIndex < config.Buses.Count)
            {
                result[config.Channels.Count + bus.BusIndex] =
                    BuildChain(EffectiveBusChain(config.Buses[bus.BusIndex]), bus.Chain);
            }
        }

        return result;
    }

    /// <summary>
    /// A bus's chain as it is actually built, which is not always the one that was configured.
    /// </summary>
    /// <remarks>
    /// D6: the limiter on a stream bus is not optional. An operator who has not added one gets one,
    /// at the end, where a limiter belongs — because a stream that clips is a stream nobody can fix
    /// afterwards and the person who would have noticed is not in the room.
    /// </remarks>
    /// <param name="bus">The bus.</param>
    /// <returns>Its chain, with the mandatory limiter appended if it is missing.</returns>
    public static IReadOnlyList<ModifierSetting> EffectiveBusChain(BusConfig bus)
    {
        if (bus.Role != BusRole.Stream)
        {
            return bus.Chain;
        }

        foreach (ModifierSetting setting in bus.Chain)
        {
            if (setting.ModifierId == MandatoryLimiterId)
            {
                return bus.Chain;
            }
        }

        // Built by hand rather than with a collection expression. A spread into IReadOnlyList makes
        // the compiler reach for MemoryMarshal, which drags System.Runtime.InteropServices into an
        // assembly a test asserts is platform-free - and that test is right to object, because the
        // day it stops objecting is the day something genuinely platform-specific slips in behind it.
        List<ModifierSetting> withLimiter = new(bus.Chain.Count + 1);

        withLimiter.AddRange(bus.Chain);

        // A stable identity rather than a fresh one each compile: the link has to survive a
        // recompile, or the limiter's envelope restarts every time somebody moves a fader.
        withLimiter.Add(new ModifierSetting { LinkId = MandatoryLimiterLinkId, ModifierId = MandatoryLimiterId });

        return withLimiter;
    }

    static ChainParams BuildChain(IReadOnlyList<ModifierSetting> settings, ModifierChain chain)
    {
        float[] targets = new float[chain.ParameterCount];
        ulong bypass = 0UL;
        int ordinal = 0;

        for (int link = 0; link < chain.Count; link++)
        {
            ModifierSetting? setting = link < settings.Count ? settings[link] : null;
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

    void AddRecordingTaps(
        GraphConfig config,
        GraphLayout layout,
        RecordingSession? recording,
        List<AudioNode> nodes)
    {
        if (recording is null)
        {
            return;
        }

        for (int channel = 0; channel < config.Channels.Count && channel < recording.Tracks.Count; channel++)
        {
            nodes.Add(new RecordingTapNode(
                recording.Tracks[channel],
                layout.PreFaderPlane(channel),
                layout.ChannelWidth(channel),
                blockFrames));
        }
    }

    /// <summary>
    /// Taps the stream bus, after everything that shapes it. E3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inputs are recorded raw so a session can be rebuilt; the stream bus is recorded
    /// <b>finished</b>, because it is the thing that actually went out. For a public body those are
    /// two different records and both are wanted: one to reconstruct what was said, one to show what
    /// was broadcast.
    /// </para>
    /// <para>
    /// Added after the bus chains and before the outputs, so what is on disk is what left the
    /// building — limiter included.
    /// </para>
    /// </remarks>
    void AddBusRecordingTap(GraphConfig config, GraphLayout layout, RecordingSession? recording, List<AudioNode> nodes)
    {
        if (recording is null || config.Buses.Count == 0)
        {
            return;
        }

        int track = config.Channels.Count;
        int bus = Math.Clamp(config.PrimaryBusIndex, 0, config.Buses.Count - 1);

        if (track >= recording.Tracks.Count)
        {
            return;
        }

        nodes.Add(new RecordingTapNode(
            recording.Tracks[track],
            layout.BusPlane(bus),
            layout.BusWidth(bus),
            blockFrames));
    }

    static AutomixParams BuildAutomix(GraphConfig config)
    {
        AutomixChannel[] channels = new AutomixChannel[config.Channels.Count];

        for (int index = 0; index < channels.Length; index++)
        {
            ChannelConfig channel = config.Channels[index];

            channels[index] = channel.ParticipatesInAutomix
                ? new AutomixChannel(true, channel.AutomixWeight)
                : AutomixChannel.Excluded;
        }

        return new AutomixParams(
            channels,
            (float)config.AutomixDepthDb,
            (float)config.AutomixResponseMilliseconds,
            config.IsAutomixBypassed);
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

            // Constant power, so a strip panned hard to one side is as loud as it was in the middle.
            // A linear law drops a centred voice by three decibels when somebody moves it, which an
            // operator then corrects on the fader, and the two controls end up fighting.
            //
            // Normalised so that the centre is unity rather than 0.707, which is not the textbook
            // pan law and is the right one here. A mono strip has always been heard at unity across
            // both sides of a stereo bus, and pan defaults to centre — so the textbook law would
            // have made every existing console three decibels quieter the moment this feature
            // landed, without anybody asking for it. The power is still constant across the travel;
            // only where the constant sits has moved.
            double angle = (Math.Clamp(channel.Pan, -1.0, 1.0) + 1.0) * (Math.PI / 4.0);
            const double CentreUnity = 1.4142135623730951;

            channels[index] = new ChannelParams(
                channel.TrimDb.ToLinearGain(),
                channel.FaderDb.ToLinearGain(),
                channel.Flags,
                (channel.Flags & ChannelFlags.MonoFold) != 0 ? 1 : Math.Max(channel.ChannelCount, 1),
                (float)(Math.Cos(angle) * CentreUnity),
                (float)(Math.Sin(angle) * CentreUnity));
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
