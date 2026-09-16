using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// U13 and U17: strips added, removed and moved while the meeting runs.
/// </summary>
/// <remarks>
/// Every one of these is really a test about sends. A strip is an index, sends are pairs of indices,
/// and an index left pointing where it used to point re-aims a microphone at the wrong destination
/// silently — which an operator discovers when the wrong voice comes out of the wrong loudspeaker.
/// </remarks>
public class ChannelOrderTests
{
    static readonly AudioDeviceId Mayor = new("null:capture:mayor");
    static readonly AudioDeviceId Lectern = new("null:capture:lectern");
    static readonly AudioDeviceId Floor = new("null:capture:floor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MovingAStripTakesItsSendsWithIt()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(2, 1, isOn: true, decibels: -3));
        console.Controller.Pump();

        // The floor microphone moves to the front. Its monitor send has to arrive with it.
        Assert.True(console.Controller.MoveChannel(2, 0));

        Assert.Equal("Floor", console.Controller.Config.Channels[0].Name);
        Assert.Equal("Mayor 180 degrees", console.Controller.Config.Channels[1].Name);
        Assert.Equal("Lectern", console.Controller.Config.Channels[2].Name);

        SendConfig send = Assert.Single(console.Controller.Config.Sends);

        Assert.Equal(0, send.ChannelIndex);
        Assert.Equal(-3, send.LevelDb);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheStripsAStripMovedPastFollowItInTheOtherDirection()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        // Mayor to the end: the lectern shuffles down into the space, and its send with it.
        Assert.True(console.Controller.MoveChannel(0, 2));

        Assert.Equal(2, console.Controller.Config.Sends.Count);
        Assert.Equal(2, SendFor(console, "Mayor 180 degrees"));
        Assert.Equal(0, SendFor(console, "Lectern"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MovingAStripNowhereChangesNothing()
    {
        ConsoleFixture console = Build();

        Assert.True(console.Controller.MoveChannel(1, 1));
        Assert.False(console.Controller.MoveChannel(0, 7));
        Assert.False(console.Controller.MoveChannel(-1, 0));

        Assert.Equal("Lectern", console.Controller.Config.Channels[1].Name);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RemovingAStripTakesItsSendsAndReAimsTheOnesAboveIt()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetSend(2, 0, isOn: true, decibels: -9));
        console.Controller.Pump();

        Assert.True(console.Controller.RemoveChannel(1));

        // The lectern's send went with it. The floor microphone's did not, and now points at the
        // slot the floor microphone actually occupies.
        SendConfig send = Assert.Single(console.Controller.Config.Sends);

        Assert.Equal(1, send.ChannelIndex);
        Assert.Equal(-9, send.LevelDb);
        Assert.Equal("Floor", console.Controller.Config.Channels[1].Name);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RemovingAStripThatIsNotThereIsRefusedRatherThanIgnored()
    {
        ConsoleFixture console = Build();

        Assert.False(console.Controller.RemoveChannel(9));
        Assert.False(console.Controller.RemoveChannel(-1));
        Assert.Equal(3, console.Controller.Config.Channels.Count);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AudioKeepsRunningAcrossAMove()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);

        console.Controller.MoveChannel(0, 2);

        // The mayor is now strip 2 and still feeding the stream. The device did not move: the strip
        // did, and the ring it reads from is bound by device rather than by position.
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);
    }

    static int SendFor(ConsoleFixture console, string name)
    {
        int index = console.Controller.Config.Channels.FindIndex(channel => channel.Name == name);

        return console.Controller.Config.Sends.Find(send => send.ChannelIndex == index).ChannelIndex;
    }

    static ConsoleFixture Build()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Mayor);
        config.InputDeviceOrder.Add(Lectern);
        config.InputDeviceOrder.Add(Floor);

        config.Channels.Add(new ChannelConfig { DeviceId = Mayor, Name = "Mayor 180 degrees" });
        config.Channels.Add(new ChannelConfig { DeviceId = Lectern, Name = "Lectern" });
        config.Channels.Add(new ChannelConfig { DeviceId = Floor, Name = "Floor" });

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });
        config.Buses.Add(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(Mayor, 1);
        console.AddDevice(Lectern, 1);
        console.AddDevice(Floor, 1);

        return console;
    }
}
