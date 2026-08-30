using System.Collections.Concurrent;
using System.Diagnostics;
using Vam.Engine.Devices;
using Vam.Engine.Graph.Nodes;
using Vam.Engine.Modifiers;
using Vam.Engine.Recording;

namespace Vam.Engine.Graph;

/// <summary>
/// The control side of the graph: takes commands, publishes snapshots, and renders blocks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Commands coalesce.</b> The loop drains everything queued before it builds anything, so a
/// fader dragged across the console — fifty updates a second — becomes a handful of snapshots
/// rather than fifty. That is the difference between the retire queue holding two objects and
/// holding a hundred.
/// </para>
/// <para>
/// <b>A parameter change never recompiles the plan.</b> Levels, flags and routing rebuild a few
/// small arrays over the plan that is already installed, so the arena, the nodes and every filter
/// history stay exactly where they are. Only a change to the shape of the console — a strip or a
/// bus appearing or disappearing — compiles a new plan.
/// </para>
/// </remarks>
public sealed class GraphController
{
    readonly ConcurrentQueue<GraphCommand> commands = new();
    readonly GraphCompiler compiler;
    readonly GraphConfig config;
    readonly List<BusOutputChannel?> busOutputs = [];

    RecordingSession? recording;

    /// <summary>Compiles the configuration and publishes the first snapshot.</summary>
    /// <param name="config">The console. Owned by this controller from here on.</param>
    /// <param name="blockFrames">Frames per block.</param>
    /// <param name="sampleRate">The rate the engine runs at, for the smoothing time.</param>
    /// <param name="registry">
    /// What modifiers exist. Null means a console with no chains, which is what everything before
    /// EPIC-04 assumed and what a test that is not about modifiers still wants.
    /// </param>
    public GraphController(GraphConfig config, int blockFrames, int sampleRate, ModifierRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        this.config = config;

        BlockTicks = (long)(Stopwatch.Frequency * (double)blockFrames / sampleRate);

        compiler = new GraphCompiler(blockFrames, sampleRate, registry);
        Publisher = new SnapshotPublisher(compiler.Compile(config, busOutputs, previous: null, recording: null));
    }

    /// <summary>Timer ticks one block of audio lasts. What the cost guard measures against.</summary>
    public long BlockTicks { get; }

    /// <summary>Where the audio thread takes its snapshot from.</summary>
    public SnapshotPublisher Publisher { get; }

    /// <summary>The configuration behind the published snapshot. Control thread only.</summary>
    public GraphConfig Config => config;

    /// <summary>
    /// Queues a change. Safe from any thread; nothing is applied until <see cref="Pump"/> runs.
    /// </summary>
    /// <param name="command">What to change.</param>
    public void Submit(GraphCommand command) => commands.Enqueue(command);

    /// <summary>
    /// Applies everything queued and publishes one snapshot. Control thread.
    /// </summary>
    /// <returns>Commands applied. Zero means nothing was published.</returns>
    public int Pump()
    {
        int applied = 0;

        while (commands.TryDequeue(out GraphCommand command))
        {
            Apply(command);
            applied++;
        }

        if (applied == 0)
        {
            // Still worth collecting: a session that has stopped changing anything would otherwise
            // hold its last retired snapshot, and its pinned arena, until something else happened.
            Publisher.Collect();
            return 0;
        }

        Publisher.Publish(GraphCompiler.Rebuild(config, Publisher.Current));

        return applied;
    }

    /// <summary>
    /// Bypasses any modifier that is costing more than its share of a block. B0b.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Control thread, and that is the whole point. The audio thread measures — a timestamp
    /// difference into a pre-allocated slot — and never decides. A modifier that has started
    /// overrunning is switched out by publishing a snapshot with its bypass bit set, which takes
    /// effect at a block boundary like every other parameter change.
    /// </para>
    /// <para>
    /// The alternative is a callback that runs late, and a callback that runs late is a dropout on
    /// the recording of a public meeting. One badly-behaved modifier should cost its own effect and
    /// nothing else.
    /// </para>
    /// </remarks>
    /// <param name="budgetFraction">
    /// How much of a block one modifier may take. A quarter leaves room for the rest of the chain,
    /// the other strips and the graph around them.
    /// </param>
    /// <returns>Links newly bypassed. Zero means nothing was published.</returns>
    public int GuardCostBudget(double budgetFraction = 0.25)
    {
        GraphSnapshot snapshot = Publisher.Current;
        int bypassed = 0;

        foreach (AudioNode node in snapshot.Plan.Nodes)
        {
            if (node is ChainNode chain)
            {
                bypassed += BypassOverruns(chain, budgetFraction);
            }
        }

        return bypassed;
    }

    /// <summary>
    /// Compiles a new plan and publishes it. For a change to the shape of the console.
    /// </summary>
    /// <remarks>
    /// Expensive by comparison — a new arena, new nodes, and every filter history starting again.
    /// Adding a strip or a bus is worth that; moving a fader is not, which is why they are different
    /// methods rather than one that decides.
    /// </remarks>
    public void Recompile() =>
        Publisher.Publish(compiler.Compile(config, busOutputs, Publisher.Current.Plan, recording));

    /// <summary>
    /// Attaches a recording session, so every strip is tapped on its way past. E3 and E4.
    /// </summary>
    /// <remarks>
    /// Takes effect on the next <see cref="Recompile"/>, because it adds nodes. Recording starting
    /// with the session rather than when somebody remembers is the whole of E4, and this is where
    /// that is arranged.
    /// </remarks>
    /// <param name="session">The session, or null to stop tapping.</param>
    public void BindRecording(RecordingSession? session)
    {
        recording = session;
        Recompile();
    }

    /// <summary>
    /// Sends a bus to a device other than the one keeping time. D7.
    /// </summary>
    /// <remarks>
    /// Takes effect on the next <see cref="Recompile"/>, because it adds a node rather than
    /// changing a number. The primary output is not bound here — it is the clock, and its audio
    /// goes straight into the render callback's own buffer with nothing in between.
    /// </remarks>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="destination">The device's rate-adapting channel, or null to unbind.</param>
    public void BindBusOutput(int busIndex, BusOutputChannel? destination)
    {
        while (busOutputs.Count <= busIndex)
        {
            busOutputs.Add(null);
        }

        busOutputs[busIndex] = destination;
    }

    /// <summary>Adds a strip. D1's counterpart for inputs; recompiles the plan.</summary>
    /// <param name="channel">The strip.</param>
    /// <returns>Its index.</returns>
    public int AddChannel(ChannelConfig channel)
    {
        config.Channels.Add(channel);
        Recompile();

        return config.Channels.Count - 1;
    }

    /// <summary>Removes a strip, and every send that came from it. U17.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Whether it was there.</returns>
    public bool RemoveChannel(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= config.Channels.Count)
        {
            return false;
        }

        config.Channels.RemoveAt(channelIndex);
        config.Sends.RemoveAll(send => send.ChannelIndex == channelIndex);

        // Sends above the gap shuffle down with it, for the same reason bus removal does it: an
        // index left pointing at its old neighbour silently re-aims a send at the wrong microphone.
        for (int index = 0; index < config.Sends.Count; index++)
        {
            SendConfig send = config.Sends[index];

            if (send.ChannelIndex > channelIndex)
            {
                config.Sends[index] = send with { ChannelIndex = send.ChannelIndex - 1 };
            }
        }

        Recompile();

        return true;
    }

    /// <summary>
    /// Moves a strip. U13.
    /// </summary>
    /// <remarks>
    /// Every send moves with it. The alternative is an operator dragging the mayor's microphone one
    /// place to the left and discovering it now feeds the wrong monitor, which is the kind of
    /// surprise that happens exactly once and is never forgiven.
    /// </remarks>
    /// <param name="fromIndex">Where it is.</param>
    /// <param name="toIndex">Where it should go.</param>
    /// <returns>Whether both indices were real.</returns>
    public bool MoveChannel(int fromIndex, int toIndex)
    {
        int count = config.Channels.Count;

        if (fromIndex < 0 || fromIndex >= count || toIndex < 0 || toIndex >= count)
        {
            return false;
        }

        if (fromIndex == toIndex)
        {
            return true;
        }

        ChannelConfig moving = config.Channels[fromIndex];

        config.Channels.RemoveAt(fromIndex);
        config.Channels.Insert(toIndex, moving);

        for (int index = 0; index < config.Sends.Count; index++)
        {
            SendConfig send = config.Sends[index];
            int moved = Reindex(send.ChannelIndex, fromIndex, toIndex);

            if (moved != send.ChannelIndex)
            {
                config.Sends[index] = send with { ChannelIndex = moved };
            }
        }

        Recompile();

        return true;
    }

    /// <summary>
    /// Adds a bus at runtime. D1.
    /// </summary>
    /// <remarks>
    /// A monitor is one of these with a different role. That is the whole reason "add a bus" and
    /// "add a monitor" are one method rather than two.
    /// </remarks>
    /// <param name="bus">The bus.</param>
    /// <returns>Its index.</returns>
    public int AddBus(BusConfig bus)
    {
        config.Buses.Add(bus);
        Recompile();

        return config.Buses.Count - 1;
    }

    /// <summary>
    /// Removes a bus at runtime, and every send that pointed at it. D1.
    /// </summary>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>Whether it was there.</returns>
    public bool RemoveBus(int busIndex)
    {
        if (busIndex < 0 || busIndex >= config.Buses.Count)
        {
            return false;
        }

        config.Buses.RemoveAt(busIndex);

        // Sends past the removed bus shuffle down with it. Leaving them pointing at their old index
        // would silently re-aim every one of them at the wrong destination.
        config.Sends.RemoveAll(send => send.BusIndex == busIndex);

        for (int index = 0; index < config.Sends.Count; index++)
        {
            SendConfig send = config.Sends[index];

            if (send.BusIndex > busIndex)
            {
                config.Sends[index] = send with { BusIndex = send.BusIndex - 1 };
            }
        }

        if (busIndex < busOutputs.Count)
        {
            busOutputs.RemoveAt(busIndex);
        }

        if (config.PrimaryBusIndex >= config.Buses.Count)
        {
            config.PrimaryBusIndex = Math.Max(config.Buses.Count - 1, 0);
        }

        Recompile();

        return true;
    }

    /// <summary>Where an index ends up after one element moved.</summary>
    /// <param name="index">The index to translate.</param>
    /// <param name="fromIndex">Where the moving element was.</param>
    /// <param name="toIndex">Where it went.</param>
    /// <returns>The index in the new order.</returns>
    static int Reindex(int index, int fromIndex, int toIndex)
    {
        if (index == fromIndex)
        {
            return toIndex;
        }

        if (fromIndex < toIndex && index > fromIndex && index <= toIndex)
        {
            return index - 1;
        }

        if (fromIndex > toIndex && index >= toIndex && index < fromIndex)
        {
            return index + 1;
        }

        return index;
    }

    /// <summary>
    /// Renders one block. This is the master clock's consumer.
    /// </summary>
    /// <remarks>Inside the audio path. One reference read, one node walk, no allocation.</remarks>
    /// <param name="inputs">One block from each device.</param>
    /// <param name="output">Where the primary output's audio goes.</param>
    /// <param name="frameCount">Frames wanted.</param>
    /// <returns>Frames written.</returns>
    public int Render(MixBlocks inputs, Span<float> output, int frameCount)
    {
        GraphSnapshot snapshot = Publisher.Acquire();

        snapshot.Plan.Render(inputs, output, snapshot, frameCount);

        return frameCount;
    }

    void Apply(GraphCommand command)
    {
        switch (command.Kind)
        {
            case GraphCommandKind.ChannelTrim:
                ReplaceChannel(command.ChannelIndex, channel => channel with { TrimDb = command.Value });
                break;

            case GraphCommandKind.ChannelFader:
                ReplaceChannel(command.ChannelIndex, channel => channel with { FaderDb = command.Value });
                break;

            case GraphCommandKind.ChannelFlag:
                ReplaceChannel(command.ChannelIndex, channel => channel with { Flags = Toggle(channel.Flags, command) });
                break;

            case GraphCommandKind.BusGain:
                ReplaceBus(command.BusIndex, bus => bus with { GainDb = command.Value });
                break;

            case GraphCommandKind.BusMuted:
                ReplaceBus(command.BusIndex, bus => bus with { IsMuted = command.IsEnabled });
                break;

            case GraphCommandKind.Send:
                ApplySend(command);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown graph command.");
        }
    }

    /// <summary>Raised when a modifier is switched out for overrunning its budget. Control thread.</summary>
    public event EventHandler<ModifierOverrun>? Overran;

    int BypassOverruns(ChainNode node, double budgetFraction)
    {
        ChainParams parameters = Publisher.Current.ChainOf(node.ChannelIndex);
        int bypassed = 0;

        for (int link = 0; link < node.Chain.Count; link++)
        {
            ModifierCost cost = node.Chain.Costs[link];

            // Ignored until it has been measured for a while. A modifier's first blocks include the
            // first-call compilation of everything it touches, and bypassing on that would switch
            // out a perfectly good denoise a hundred milliseconds into every session.
            if (cost.BlockCount < 64 || parameters.IsBypassed(link))
            {
                continue;
            }

            double fraction = cost.FractionOfBudget(BlockTicks);

            if (fraction <= budgetFraction)
            {
                continue;
            }

            parameters = parameters.WithBypass(link, isBypassed: true);
            bypassed++;

            Overran?.Invoke(this, new ModifierOverrun(
                node.ChannelIndex,
                link,
                node.Chain.Modifiers[link].Descriptor.Name,
                fraction));
        }

        if (bypassed > 0)
        {
            Publisher.Publish(Publisher.Current.WithChain(node.ChannelIndex, parameters));
        }

        return bypassed;
    }

    static ChannelFlags Toggle(ChannelFlags flags, GraphCommand command) =>
        command.IsEnabled ? flags | command.Flag : flags & ~command.Flag;

    void ReplaceChannel(int index, Func<ChannelConfig, ChannelConfig> change)
    {
        if (index >= 0 && index < config.Channels.Count)
        {
            config.Channels[index] = change(config.Channels[index]);
        }
    }

    void ReplaceBus(int index, Func<BusConfig, BusConfig> change)
    {
        if (index >= 0 && index < config.Buses.Count)
        {
            config.Buses[index] = change(config.Buses[index]);
        }
    }

    void ApplySend(GraphCommand command)
    {
        SendConfig send = new(command.ChannelIndex, command.BusIndex, command.IsEnabled, command.Value);

        for (int index = 0; index < config.Sends.Count; index++)
        {
            if (config.Sends[index].ChannelIndex == send.ChannelIndex
                && config.Sends[index].BusIndex == send.BusIndex)
            {
                config.Sends[index] = send;
                return;
            }
        }

        config.Sends.Add(send);
    }
}
