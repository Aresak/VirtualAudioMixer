using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// EPIC-03. Audio reaching buses, and the routing rules that decide how much of it.
/// </summary>
public class MixGraphTests
{
    static readonly AudioDeviceId MayorMicrophone = new("null:capture:mayor");
    static readonly AudioDeviceId JabraMicrophone = new("null:capture:jabra");
    static readonly AudioDeviceId JabraSpeaker = new("null:render:jabra");
    static readonly AudioDeviceId StreamOutput = new("null:render:stream");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnInputAtUnityReachesTheOutputThroughItsSend()
    {
        GraphConfig config = OneMicrophoneOneBus();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        // Unity in, unity out. Every gain in the chain is one, so anything else here is a stage
        // quietly scaling something it should not.
        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASendThatIsOffCarriesNothing()
    {
        GraphConfig config = OneMicrophoneOneBus();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0f, console.OutputPeak());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MixMinusExcludesADevicesOwnMicrophoneFromItsOwnSpeaker()
    {
        GraphConfig config = SpeakerphoneConsole();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.AddDevice(JabraMicrophone, 1);

        // Bus 0 goes to the Jabra's speaker. Channel 1 is the Jabra's own microphone.
        Assert.Equal(SendState.ExcludedMixMinus, console.Controller.Publisher.Current.Sends.StateOf(1, 0));
        Assert.Equal(SendState.Off, console.Controller.Publisher.Current.Sends.StateOf(0, 0));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOperatorCannotSwitchAMixMinusExclusionBackOn()
    {
        GraphConfig config = SpeakerphoneConsole();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.AddDevice(JabraMicrophone, 1);

        // The console asks. The compiler is where the answer is no - which is the whole design:
        // an exclusion that could be clicked back on is one careless click from a councillor
        // hearing themselves late in front of a public gallery.
        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        SendMatrix sends = console.Controller.Publisher.Current.Sends;

        Assert.Equal(SendState.ExcludedMixMinus, sends.StateOf(1, 0));
        Assert.Equal(0f, sends.GainOf(1, 0));

        console.Feed(1, 0.9f);
        console.RenderUntilSettled();

        Assert.Equal(0f, console.OutputPeak());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSameMicrophoneStillReachesAnotherBus()
    {
        GraphConfig config = SpeakerphoneConsole();

        // A second bus, going somewhere else entirely. Mix-minus must exclude one relationship, not
        // silence the microphone.
        config.Buses.Add(new BusConfig
        {
            Name = "Stream",
            Role = BusRole.Stream,
            ChannelCount = 2,
            OutputDeviceId = StreamOutput
        });

        config.PrimaryBusIndex = 1;

        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.AddDevice(JabraMicrophone, 1);

        console.Controller.Submit(GraphCommand.SetSend(1, 1, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(1, 0.4f);
        console.RenderUntilSettled();

        Assert.Equal(0.4f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SoloSilencesOtherStripsOnAnOutputBus()
    {
        GraphConfig config = TwoMicrophonesOneBus(BusRole.Output);
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.AddDevice(JabraMicrophone, 1);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.25f);
        console.Feed(1, 0.25f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);

        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.Soloed, isEnabled: true));
        console.Controller.Pump();
        console.RenderUntilSettled();

        Assert.Equal(0.25f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SoloNeverReachesTheStreamBus()
    {
        GraphConfig config = TwoMicrophonesOneBus(BusRole.Stream);
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.AddDevice(JabraMicrophone, 1);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.Soloed, isEnabled: true));
        console.Controller.Pump();

        console.Feed(0, 0.25f);
        console.Feed(1, 0.25f);
        console.RenderUntilSettled();

        // One click silencing a public broadcast is a mistake that ends up in the minutes. Solo is
        // the operator's monitoring tool and it stops at the monitors.
        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMonitorBusIgnoresTheFader()
    {
        GraphConfig config = OneMicrophoneOneBus();
        config.Buses[0] = config.Buses[0] with { Role = BusRole.Monitor };

        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);

        // The operator pulls the fader right down for the stream. The person wearing the headphones
        // must not notice, which is the entire reason a monitor takes the pre-fader tap.
        console.Controller.Submit(GraphCommand.SetFader(0, -60));
        console.Controller.Pump();
        console.RenderUntilSettled();

        Assert.Equal(0.5f, console.OutputPeak(), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MutingIsMuting()
    {
        GraphConfig config = OneMicrophoneOneBus();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.Muted, isEnabled: true));
        console.Controller.Pump();
        console.RenderUntilSettled();

        Assert.Equal(0f, console.OutputPeak(), 0.0001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFaultedStripIsSilentWithoutAnybodyMutingIt()
    {
        GraphConfig config = OneMicrophoneOneBus();
        ConsoleFixture console = new(config);

        console.AddDevice(MayorMicrophone, 1);
        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetFlag(0, ChannelFlags.Faulted, isEnabled: true));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled();

        // I1 fault isolation. A device that failed takes down its own strip and reaches the mix as
        // silence rather than as whatever was left in a buffer.
        Assert.Equal(0f, console.OutputPeak(), 0.0001f);
    }

    static GraphConfig OneMicrophoneOneBus()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(MayorMicrophone);
        config.Channels.Add(new ChannelConfig { DeviceId = MayorMicrophone, Name = "Mayor 180 degrees" });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 2 });

        return config;
    }

    static GraphConfig TwoMicrophonesOneBus(BusRole role)
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(MayorMicrophone);
        config.InputDeviceOrder.Add(JabraMicrophone);
        config.Channels.Add(new ChannelConfig { DeviceId = MayorMicrophone, Name = "Mayor 180 degrees" });
        config.Channels.Add(new ChannelConfig { DeviceId = JabraMicrophone, Name = "Jabra" });
        config.Buses.Add(new BusConfig { Name = "Bus", Role = role, ChannelCount = 2 });

        return config;
    }

    static GraphConfig SpeakerphoneConsole()
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(MayorMicrophone);
        config.InputDeviceOrder.Add(JabraMicrophone);
        config.Channels.Add(new ChannelConfig { DeviceId = MayorMicrophone, Name = "Mayor 180 degrees" });
        config.Channels.Add(new ChannelConfig { DeviceId = JabraMicrophone, Name = "Jabra" });

        config.Buses.Add(new BusConfig
        {
            Name = "Jabra feed",
            Role = BusRole.Output,
            ChannelCount = 2,
            OutputDeviceId = JabraSpeaker
        });

        // The declaration everything else falls out of: this microphone and this speaker belong to
        // the same person.
        config.EndpointPairs.Add(new EndpointPair(JabraMicrophone, JabraSpeaker));

        return config;
    }
}
