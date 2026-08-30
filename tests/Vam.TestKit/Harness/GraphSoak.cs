using System.Diagnostics;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Diagnostics;
using Vam.Engine.Graph;
using Vam.Engine.Modifiers;
using Vam.TestKit.Graph;

namespace Vam.TestKit.Harness;

/// <summary>
/// I5. Drives the whole graph from synthetic signal, faster than realtime, and reports.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole graph, which is the point.</b> The drift simulation drives the device layer and
/// proves the rings and the servo behave over hours. It never touches a modifier chain, an
/// automixer or a bus, so the things most likely to allocate — a chain being rebuilt, a parameter
/// being smoothed, a snapshot being published under a render — were never soaked at all.
/// </para>
/// <para>
/// <b>Unattended and reported.</b> Drift bugs do not show up in five minutes, and the person who
/// runs this will not be watching. It returns numbers rather than throwing, so the caller decides
/// what counts as a failure and can print the numbers either way.
/// </para>
/// </remarks>
public sealed class GraphSoak
{
    const int BlockFrames = ConsoleFixture.BlockFrames;
    const int SampleRate = ConsoleFixture.SampleRate;

    readonly ConsoleFixture console;
    readonly AudioThreadAllocations allocations = new();
    readonly CallbackHistogram callbacks = new();
    readonly int channels;

    double phase;

    /// <summary>Builds a console of a given size, every strip with a full chain.</summary>
    /// <param name="channelCount">How many strips.</param>
    /// <param name="withChains">Whether to give each strip the chain EPIC-05 argues for.</param>
    public GraphSoak(int channelCount = 5, bool withChains = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);

        channels = channelCount;
        console = Build(channelCount, withChains);
    }

    /// <summary>The console being driven, for a caller that wants to change something mid-soak.</summary>
    public ConsoleFixture Console => console;

    /// <summary>
    /// Runs for a simulated duration and reports.
    /// </summary>
    /// <param name="duration">How much audio to push through.</param>
    /// <param name="disturbEvery">
    /// How often to change something while it runs, or null for a quiet soak. A soak where nothing
    /// ever changes never exercises the snapshot swap, which is the one place a render and a
    /// configuration change can meet.
    /// </param>
    /// <returns>What happened.</returns>
    public GraphSoakReport Run(TimeSpan duration, TimeSpan? disturbEvery = null)
    {
        long blocks = (long)(duration.TotalSeconds * SampleRate / BlockFrames);
        long disturbEveryBlocks = disturbEvery is { } every
            ? Math.Max((long)(every.TotalSeconds * SampleRate / BlockFrames), 1)
            : long.MaxValue;

        long blockTicks = (long)((double)BlockFrames / SampleRate * Stopwatch.Frequency);
        long started = Stopwatch.GetTimestamp();
        int disturbances = 0;

        // Warmed before anything is measured, and warmed through every path the soak will take.
        //
        // The first version warmed the render only, and the measurement came back non-deterministic:
        // sometimes zero bytes, sometimes eight kilobytes. The eight kilobytes were the just-in-time
        // compiler running on this thread the first time a block was rendered against a freshly
        // recompiled plan - a cost the real engine pays once at startup and this harness was
        // charging to the audio path. So the disturbances are warmed too, and anything left after
        // this is genuinely the graph allocating.
        Feed();

        for (int warm = 0; warm < 200; warm++)
        {
            console.Render();
        }

        for (int warm = 0; warm < 8; warm++)
        {
            Disturb(warm);
            console.Render();
        }

        for (int warm = 0; warm < 200; warm++)
        {
            console.Render();
        }

        allocations.Clear();
        callbacks.Clear();

        for (long block = 0; block < blocks; block++)
        {
            Feed();

            long at = Stopwatch.GetTimestamp();

            allocations.Begin();
            console.Render();
            allocations.End();

            callbacks.Record(Stopwatch.GetTimestamp() - at, blockTicks);

            if ((block + 1) % disturbEveryBlocks == 0)
            {
                Disturb(disturbances++);
            }
        }

        return new GraphSoakReport(
            duration,
            blocks,
            channels,
            allocations.TotalBytes,
            callbacks.WorstFraction,
            callbacks.Overruns,
            disturbances,
            TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency));
    }

    /// <summary>
    /// Changes something, the way an operator would.
    /// </summary>
    /// <remarks>
    /// Faders, sends and mutes go through the command queue and republish a snapshot; a recompile
    /// rebuilds the plan and every filter in it. Both happen during a meeting and both are the kind
    /// of thing that allocates if somebody is careless, which is exactly what this is looking for.
    /// </remarks>
    void Disturb(int index)
    {
        int channel = index % channels;

        switch (index % 4)
        {
            case 0:
                console.Controller.Submit(GraphCommand.SetFader(channel, index % 2 == 0 ? -6 : 0));
                break;

            case 1:
                console.Controller.Submit(GraphCommand.SetSend(channel, 0, index % 3 != 0, 0));
                break;

            case 2:
                console.Controller.Submit(GraphCommand.SetFlag(channel, ChannelFlags.Muted, index % 5 == 0));
                break;

            default:
                console.Controller.Recompile();
                return;
        }

        console.Controller.Pump();
    }

    /// <summary>
    /// Fills every device buffer with something that is not a constant.
    /// </summary>
    /// <remarks>
    /// A constant is not audio, and several things in the graph behave differently on one: the
    /// detector rejects it, the high-pass removes it entirely, and a denoise sees no spectrum at
    /// all. A soak on constants would exercise the arithmetic and none of the decisions.
    /// </remarks>
    void Feed()
    {
        for (int channel = 0; channel < channels; channel++)
        {
            // Written into the fixture's own buffer rather than through a callback. A lambda here
            // captures two locals and allocates a closure on every block, which is outside the
            // measured region but still makes the collector run during it - and a soak whose own
            // rubbish causes the pauses it is measuring is not measuring anything.
            Span<float> buffer = console.DeviceBuffer(channel);
            double step = 0.02 * (channel + 1);

            for (int frame = 0; frame < buffer.Length; frame++)
            {
                buffer[frame] = (float)(Math.Sin(phase + (frame * step)) * 0.3);
            }
        }

        phase += 0.31;
    }

    static ConsoleFixture Build(int channelCount, bool withChains)
    {
        GraphConfig config = new() { IsAutomixBypassed = false };

        for (int channel = 0; channel < channelCount; channel++)
        {
            AudioDeviceId device = new($"null:capture:soak{channel}");

            config.InputDeviceOrder.Add(device);

            ChannelConfig strip = new()
            {
                DeviceId = device,
                Name = $"Microphone {channel + 1}",
                ParticipatesInAutomix = true
            };

            if (withChains)
            {
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.highpass" });
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.gate" });
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.denoise" });
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.equaliser" });
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.adaptivegain" });
                strip.Chain.Add(new ModifierSetting { ModifierId = "vam.compressor" });
            }

            config.Channels.Add(strip);
            config.Sends.Add(new SendConfig(channel, 0, IsOn: true, LevelDb: 0));
            config.Sends.Add(new SendConfig(channel, 1, IsOn: true, LevelDb: 0));
        }

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, ChannelCount = 2 });
        config.Buses.Add(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        ConsoleFixture console = new(config, ModifierRegistry.CreateDefault());

        for (int channel = 0; channel < channelCount; channel++)
        {
            console.AddDevice(new AudioDeviceId($"null:capture:soak{channel}"), 1);
        }

        return console;
    }
}
