using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// B7's pre-fade listen: the operator's inspection tool, and the rules that keep it off the stream.
/// </summary>
/// <remarks>
/// It was a declared flag that nothing read for the whole of the build. The graph had
/// <see cref="ChannelFlags.PreFadeListen"/>, the console had no control for it, and no bus did
/// anything with it.
/// </remarks>
public class PreFadeListenTests
{
    static readonly AudioDeviceId Mayor = new("null:capture:mayor");
    static readonly AudioDeviceId Lectern = new("null:capture:lectern");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripFadedToNothingIsStillHeardOnAMonitor()
    {
        ConsoleFixture console = Build();

        // All the way down. This is the case the whole feature exists for: a microphone an operator
        // wants to check without putting it into the room first.
        console.Controller.Submit(GraphCommand.SetFader(0, -100));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.PreFadeListen, isEnabled: true));
        console.Controller.Pump();

        console.Feed(0, 0.5f);

        float monitor = RenderMonitor(console);

        Assert.True(monitor > 0.4f, $"The monitor heard {monitor} from a strip that is faded down.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItNeverReachesTheStream()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetFader(0, -100));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.PreFadeListen, isEnabled: true));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        // An operator checking a microphone must not be broadcasting the check. The stream carries
        // the faded strip, which is silence, and nothing else changed about it.
        Assert.True(console.OutputPeak() < 0.01f, $"The stream carried {console.OutputPeak()} during a PFL.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ListeningToOneStripSilencesTheOthersOnTheMonitor()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(1, 1, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.PreFadeListen, isEnabled: true));
        console.Controller.Pump();

        // The lectern is routed to the monitor and the mayor is not; under PFL the monitor should
        // carry the mayor and nothing else. That is what makes it an inspection tool rather than
        // another send.
        console.Feed(0, 0.5f);
        console.Feed(1, 0.5f);

        float monitor = RenderMonitor(console);

        Assert.Equal(0.5f, monitor, 0.02f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ReleasingItPutsTheMonitorBack()
    {
        ConsoleFixture console = Build();

        console.Controller.Submit(GraphCommand.SetSend(1, 1, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.PreFadeListen, isEnabled: true));
        console.Controller.Pump();

        console.Feed(1, 0.5f);
        RenderMonitor(console);

        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.PreFadeListen, isEnabled: false));
        console.Controller.Pump();

        // Back to whatever the monitor was carrying. A PFL that had to be undone by rebuilding the
        // monitor's sources would be one nobody dared use during a meeting.
        float monitor = RenderMonitor(console);

        Assert.Equal(0.5f, monitor, 0.02f);
    }

    /// <summary>Renders and returns the peak the monitor bus carried.</summary>
    static float RenderMonitor(ConsoleFixture console)
    {
        float peak = 0f;

        for (int block = 0; block < ConsoleFixture.BlocksToSettle * 2; block++)
        {
            console.Render();
        }

        for (int block = 0; block < ConsoleFixture.BlocksToSettle; block++)
        {
            console.Render();
            peak = Math.Max(peak, console.BusPeak(1));
        }

        return peak;
    }

    static ConsoleFixture Build()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Mayor);
        config.InputDeviceOrder.Add(Lectern);

        config.Channels.Add(new ChannelConfig { DeviceId = Mayor, Name = "Mayor 180 degrees" });
        config.Channels.Add(new ChannelConfig { DeviceId = Lectern, Name = "Lectern" });

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });
        config.Buses.Add(new BusConfig { Name = "Monitor", Role = BusRole.Monitor, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(Mayor, 1);
        console.AddDevice(Lectern, 1);

        return console;
    }
}
