using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Vam.Server.Engine;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// I1, which EPIC-12 calls the single most important behaviour in the project: an error inside one
/// strip mutes that strip and never reaches the mix.
/// </summary>
/// <remarks>
/// The graph has always honoured <see cref="ChannelFlags.Faulted"/> — a faulted strip is silent
/// without anybody muting it. What did not exist was anything that <b>set</b> the flag, which made
/// it a safety mechanism nothing armed. These tests are about the arming.
/// </remarks>
public class FaultIsolationTests
{
    const int BlockFrames = 120;

    static readonly TimeSpan Correction = TimeSpan.FromMilliseconds(250);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatFailsMutesItsStripAndNothingElse()
    {
        using DriftSimulation simulation = new(BlockFrames, 1024, Correction);

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 0);
        simulation.AddDevice("Lectern", driftPpm: 0);

        GraphController graph = BuildGraph();
        RecordingLoggerFactory loggers = new();
        FaultWatch watch = new(graph, simulation.Registry, loggers.CreateTyped<FaultWatch>());

        watch.Poll();

        Assert.Equal(0, watch.FaultedCount);
        Assert.Equal(ChannelFlags.None, graph.Config.Channels[0].Flags);

        simulation.Channels[0].State = DeviceStreamState.Faulted;
        watch.Poll();

        // The one strip, and only the one strip. A fault that reached the mix would take the
        // meeting with it, which is the whole reason this behaviour is ranked above every DSP
        // feature in the project.
        Assert.Equal(1, watch.FaultedCount);
        Assert.Equal(ChannelFlags.Faulted, graph.Config.Channels[0].Flags);
        Assert.Equal(ChannelFlags.None, graph.Config.Channels[1].Flags);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheLogNamesTheStripAndSaysTheSessionContinues()
    {
        using DriftSimulation simulation = new(BlockFrames, 1024, Correction);

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 0);

        GraphController graph = BuildGraph(1);
        RecordingLoggerFactory loggers = new();
        FaultWatch watch = new(graph, simulation.Registry, loggers.CreateTyped<FaultWatch>());

        watch.Poll();

        simulation.Channels[0].State = DeviceStreamState.Faulted;
        watch.Poll();

        // Named, because a row of indices is not something an operator can act on at ten to seven.
        Assert.True(loggers.Mentions("Mayor 180 degrees"), "The log did not name the strip that was muted.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItSaysSoOncePerTransitionRatherThanOncePerTick()
    {
        using DriftSimulation simulation = new(BlockFrames, 1024, Correction);

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 0);

        GraphController graph = BuildGraph(1);
        RecordingLoggerFactory loggers = new();
        FaultWatch watch = new(graph, simulation.Registry, loggers.CreateTyped<FaultWatch>());

        watch.Poll();
        simulation.Channels[0].State = DeviceStreamState.Faulted;

        for (int tick = 0; tick < 50; tick++)
        {
            watch.Poll();
        }

        // A device that has failed fails on every tick. Fifty identical lines would push the first
        // one - the only one with any information in it - off the top of the log.
        Assert.Equal(1, loggers.CountMentioning("its device failed"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripComesBackWhenItsDeviceDoes()
    {
        using DriftSimulation simulation = new(BlockFrames, 1024, Correction);

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 0);

        GraphController graph = BuildGraph(1);
        RecordingLoggerFactory loggers = new();
        FaultWatch watch = new(graph, simulation.Registry, loggers.CreateTyped<FaultWatch>());

        simulation.Channels[0].State = DeviceStreamState.Absent;
        watch.Poll();

        Assert.Equal(ChannelFlags.Faulted, graph.Config.Channels[0].Flags);

        simulation.Channels[0].State = DeviceStreamState.Running;
        watch.Poll();

        // Unplugged is not broken. The supervisor is already waiting for it, and a strip that stayed
        // muted after its microphone came back would need somebody to notice and un-mute it - during
        // a meeting, on a console with fifteen other strips on it.
        Assert.Equal(ChannelFlags.None, graph.Config.Channels[0].Flags);
        Assert.Equal(0, watch.FaultedCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMutedStripStaysMutedWhenItsDeviceRecovers()
    {
        using DriftSimulation simulation = new(BlockFrames, 1024, Correction);

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 0);

        GraphController graph = BuildGraph(1);
        RecordingLoggerFactory loggers = new();
        FaultWatch watch = new(graph, simulation.Registry, loggers.CreateTyped<FaultWatch>());

        graph.Submit(GraphCommand.SetFlag(0, ChannelFlags.Muted, isEnabled: true));
        graph.Pump();

        simulation.Channels[0].State = DeviceStreamState.Absent;
        watch.Poll();

        simulation.Channels[0].State = DeviceStreamState.Running;
        watch.Poll();

        // The operator's mute is theirs. Clearing the fault must not clear a decision somebody made
        // deliberately, or a strip muted for a reason comes back on by itself.
        Assert.Equal(ChannelFlags.Muted, graph.Config.Channels[0].Flags);
    }

    static GraphController BuildGraph(int channels = 2)
    {
        GraphConfig config = new();

        for (int index = 0; index < channels; index++)
        {
            AudioDeviceId device = new($"null:capture:{index}");

            config.InputDeviceOrder.Add(device);
            config.Channels.Add(new ChannelConfig
            {
                DeviceId = device,
                Name = index == 0 ? "Mayor 180 degrees" : "Lectern"
            });
        }

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        return new GraphController(config, BlockFrames, 48000);
    }
}
