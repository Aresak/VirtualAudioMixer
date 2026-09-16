using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// D1 and D7: buses created and removed while audio runs, and buses reaching devices other than the
/// one keeping time.
/// </summary>
public class BusRoutingTests
{
    static readonly AudioDeviceId Microphone = new("null:capture:mayor");
    static readonly AudioDeviceId Headphones = new("null:render:monitor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABusCanBeAddedAndRemovedWithoutStoppingAudio()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);

        int monitor = console.Controller.AddBus(new BusConfig
        {
            Name = "Councillor headphones",
            Role = BusRole.Monitor,
            ChannelCount = 2
        });

        Assert.Equal(1, monitor);

        // A new plan means new nodes and new smoothing state, so the level ramps up again from
        // zero. That is honest rather than a defect: the graph really was rebuilt.
        console.RenderUntilSettled();
        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);

        Assert.True(console.Controller.RemoveBus(monitor));

        console.RenderUntilSettled();
        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RemovingABusReAimsTheSendsAboveItRatherThanLeavingThemDangling()
    {
        ConsoleFixture console = Build();

        console.Controller.AddBus(new BusConfig { Name = "Middle", Role = BusRole.Output, ChannelCount = 2 });
        console.Controller.AddBus(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        console.Controller.Submit(GraphCommand.SetSend(0, 2, isOn: true, decibels: -6));
        console.Controller.Pump();

        console.Controller.RemoveBus(1);

        // The monitor was bus 2 and is now bus 1. A send left pointing at 2 would be aimed at
        // nothing, or worse, at whatever moved into that slot next.
        SendConfig send = Assert.Single(console.Controller.Config.Sends);

        Assert.Equal(1, send.BusIndex);
        Assert.True(send.IsOn);
        Assert.Equal(-6, send.LevelDb);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABusBoundToAnotherDeviceReachesItThroughItsOwnRing()
    {
        ConsoleFixture console = Build();

        int monitor = console.Controller.AddBus(new BusConfig
        {
            Name = "Councillor headphones",
            Role = BusRole.Monitor,
            ChannelCount = 2,
            OutputDeviceId = Headphones
        });

        BusOutputChannel destination = new(
            Headphones,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = ConsoleFixture.SampleRate,
                ChannelCount = 2,
                BlockFrames = ConsoleFixture.BlockFrames,
                RingCapacityFrames = 4096,
                TargetFillFrames = 1024
            },
            NullLogger<DeviceInputChannel>.Instance);

        console.Controller.BindBusOutput(monitor, destination);
        console.Controller.Recompile();

        console.Controller.Submit(GraphCommand.SetSend(0, monitor, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);

        // Drained every block, the way the device's own thread would. Rendering without draining
        // fills the ring and then every further write is refused, so what comes out is the start of
        // the smoothing ramp rather than the steady state - which is correct behaviour for an
        // overrun and a useless thing to assert a level against.
        float[] played = new float[ConsoleFixture.BlockFrames * 2];
        float peak = 0f;

        for (int block = 0; block < ConsoleFixture.BlocksToSettle * 2; block++)
        {
            console.Render();
            destination.Fill(played, ConsoleFixture.BlockFrames);
        }

        foreach (float sample in played)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // The mix thread wrote into the ring; the device's own thread took it out. That hand-off is
        // the whole reason a second output goes through one of these rather than into a buffer.
        Assert.Equal(0.5f, peak, 0.01f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANewBusIsFedByEveryStripRatherThanArrivingSilent()
    {
        ConsoleFixture console = Build();

        int monitor = console.Controller.AddBus(new BusConfig
        {
            Name = "Councillor headphones",
            Role = BusRole.Monitor,
            ChannelCount = 2,
            OutputDeviceId = Headphones
        });

        BusOutputChannel destination = new(
            Headphones,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = ConsoleFixture.SampleRate,
                ChannelCount = 2,
                BlockFrames = ConsoleFixture.BlockFrames,
                RingCapacityFrames = 4096,
                TargetFillFrames = 1024
            },
            NullLogger<DeviceInputChannel>.Instance);

        console.Controller.BindBusOutput(monitor, destination);
        console.Controller.Recompile();

        // No SetSend. That is the whole test: somebody adds a monitor, plugs in headphones, and
        // hears the room - rather than silence and a grid of switches they have to find first.
        console.Feed(0, 0.5f);

        float[] played = new float[ConsoleFixture.BlockFrames * 2];
        float peak = 0f;

        for (int block = 0; block < ConsoleFixture.BlocksToSettle * 2; block++)
        {
            console.Render();
            destination.Fill(played, ConsoleFixture.BlockFrames);
        }

        foreach (float sample in played)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        Assert.Equal(0.5f, peak, 0.01f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANewStripReachesEveryBusThatAlreadyExists()
    {
        ConsoleFixture console = Build();

        console.Controller.AddBus(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        int strip = console.Controller.AddChannel(new ChannelConfig { DeviceId = Microphone, Name = "Lectern" });

        // The same failure the other way round: a microphone added during a meeting that goes
        // nowhere until somebody works out which switches it needs.
        foreach (int bus in (ReadOnlySpan<int>)[0, 1])
        {
            SendConfig send = Assert.Single(
                console.Controller.Config.Sends,
                candidate => candidate.ChannelIndex == strip && candidate.BusIndex == bus);

            Assert.True(send.IsOn);
            Assert.Equal(0, send.LevelDb);
        }
    }

    static ConsoleFixture Build()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Microphone);
        config.Channels.Add(new ChannelConfig { DeviceId = Microphone, Name = "Mayor 180 degrees" });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(Microphone, 1);

        return console;
    }
}
