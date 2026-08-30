using System.Collections.Concurrent;
using Vam.Engine.Devices;

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

    /// <summary>Compiles the configuration and publishes the first snapshot.</summary>
    /// <param name="config">The console. Owned by this controller from here on.</param>
    /// <param name="blockFrames">Frames per block.</param>
    /// <param name="sampleRate">The rate the engine runs at, for the smoothing time.</param>
    public GraphController(GraphConfig config, int blockFrames, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(config);

        this.config = config;

        compiler = new GraphCompiler(blockFrames, sampleRate);
        Publisher = new SnapshotPublisher(compiler.Compile(config));
    }

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
    /// Compiles a new plan and publishes it. For a change to the shape of the console.
    /// </summary>
    /// <remarks>
    /// Expensive by comparison — a new arena, new nodes, and every filter history starting again.
    /// Adding a strip or a bus is worth that; moving a fader is not, which is why they are different
    /// methods rather than one that decides.
    /// </remarks>
    public void Recompile() => Publisher.Publish(compiler.Compile(config));

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
