using Vam.Engine.Automix;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.TestKit.Allocations;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Automix;

/// <summary>
/// EPIC-06. Gain sharing across the microphones in the room.
/// </summary>
/// <remarks>
/// The maths is a page and the tuning is the work. What is tested here is the maths — that the
/// shares add up, that the count is continuous, that the depth is a floor. Whether it is
/// transparent on a real meeting is a listening judgement and is owed separately.
/// </remarks>
public class AutomixTests
{
    static readonly AudioDeviceId First = new("null:capture:mayor");
    static readonly AudioDeviceId Second = new("null:capture:lectern");
    static readonly AudioDeviceId Third = new("null:capture:jabra");
    static readonly AudioDeviceId Return = new("null:capture:online");

    /// <summary>
    /// Blocks to render before the automixer has arrived.
    /// </summary>
    /// <remarks>
    /// Far more than a parameter change needs. The automixer's own release is the response knob -
    /// a hundred and twenty milliseconds - which is six times slower than the parameter smoothing,
    /// and asserting a settled gain before it has settled measures the ramp instead.
    /// </remarks>
    const int BlocksToArrive = 800;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheLoudestMicrophoneTakesMostOfTheGain()
    {
        ConsoleFixture console = Build();

        console.Feed(0, 0.5f);
        console.Feed(1, 0.02f);
        Settle(console);

        AutomixState state = StateOf(console);

        // Somebody speaking and somebody not. The share is not a level - it is how much of the
        // available gain this microphone is holding.
        Assert.True(
            state.Shares[0] > 0.95f,
            $"The speaker only held {state.Shares[0]:F3} of the gain.");

        Assert.True(state.Shares[1] < 0.05f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SharesAlwaysAddUpToOne()
    {
        ConsoleFixture console = Build();

        console.Feed(0, 0.3f);
        console.Feed(1, 0.2f);
        console.Feed(2, 0.1f);
        Settle(console);

        AutomixState state = StateOf(console);
        float total = 0f;

        foreach (float share in state.Shares)
        {
            total += share;
        }

        Assert.Equal(1f, total, 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheOpenMicrophoneCountIsContinuousRatherThanAStep()
    {
        ConsoleFixture console = Build();

        console.Feed(0, 0.3f);
        console.Feed(1, 0.3f);
        console.Feed(2, 0.3f);
        Settle(console);

        // Three microphones sharing equally is exactly three. That is the property a threshold count
        // does not have, and it is why the count comes from the participation ratio instead.
        Assert.Equal(3f, StateOf(console).NumberOfOpenMicrophones, 0.05f);

        console.Feed(1, 0.0f);
        console.Feed(2, 0.0f);
        Settle(console);

        Assert.Equal(1f, StateOf(console).NumberOfOpenMicrophones, 0.05f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheCountMovesSmoothlyAcrossTheBoundaryAThresholdWouldStepAt()
    {
        ConsoleFixture console = Build();

        List<float> counts = [];

        console.Feed(0, 0.3f);

        // A second microphone creeping up from nothing to level with the first. A count of channels
        // above a threshold jumps from one to two somewhere in here and steps the whole bus by three
        // decibels; the participation ratio has to walk.
        for (int step = 0; step <= 10; step++)
        {
            console.Feed(1, 0.3f * step / 10f);
            Settle(console);
            counts.Add(StateOf(console).NumberOfOpenMicrophones);
        }

        for (int index = 1; index < counts.Count; index++)
        {
            Assert.True(
                counts[index] - counts[index - 1] < 0.4f,
                $"The count jumped from {counts[index - 1]:F2} to {counts[index]:F2}, which is a step.");
        }

        Assert.True(counts[^1] > 1.9f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DepthIsAFloorRatherThanAMute()
    {
        ConsoleFixture console = Build(depthDb: -12.0);

        console.Feed(0, 0.5f);
        console.Feed(1, 0.0f);
        Settle(console);

        AutomixState state = StateOf(console);

        // Turning an unused microphone all the way off makes the room sound dead between speakers,
        // and then the whole ambience arrives with whoever talks next.
        Assert.Equal(-12f, state.GainsDb[1], 0.5f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripThatDoesNotParticipateIsLeftAlone()
    {
        ConsoleFixture console = Build();

        // Strip 3 is the online return in this console, and it is loud.
        console.Feed(0, 0.1f);
        console.Feed(3, 0.9f);
        Settle(console);

        AutomixState state = StateOf(console);

        Assert.Equal(0f, state.Shares[3]);
        Assert.Equal(0f, state.GainsDb[3], 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void BypassPutsEverythingBackToUnity()
    {
        ConsoleFixture console = Build();

        console.Feed(0, 0.5f);
        console.Feed(1, 0.01f);
        Settle(console);

        Assert.True(StateOf(console).GainsDb[1] < -1f);

        console.Controller.Config.IsAutomixBypassed = true;
        console.Controller.Submit(GraphCommand.SetFader(0, 0));
        console.Controller.Pump();
        console.Render();

        AutomixState state = StateOf(console);

        // C10. One branch at the top, reachable from any view, and the console shows unity rather
        // than the shares it happened to be holding when somebody switched it off.
        Assert.Equal(0f, state.GainsDb[0], 0.001f);
        Assert.Equal(0f, state.GainsDb[1], 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SharingGainAllocatesNothing()
    {
        ConsoleFixture console = Build();

        console.Feed(0, 0.3f);
        console.Feed(1, 0.2f);
        Settle(console);

        AllocationAssert.None(console, static fixture => fixture.Render());
    }

    static void Settle(ConsoleFixture console)
    {
        for (int block = 0; block < BlocksToArrive; block++)
        {
            console.Render();
        }
    }

    static AutomixState StateOf(ConsoleFixture console)
    {
        foreach (AudioNode node in console.Controller.Publisher.Current.Plan.Nodes)
        {
            if (node is AutomixNode automix)
            {
                return automix.State;
            }
        }

        throw new InvalidOperationException("The console has no automixer.");
    }

    static ConsoleFixture Build(double depthDb = -15.0)
    {
        GraphConfig config = new()
        {
            IsAutomixBypassed = false,
            AutomixDepthDb = depthDb,
            AutomixResponseMilliseconds = 120.0
        };

        config.InputDeviceOrder.Add(First);
        config.InputDeviceOrder.Add(Second);
        config.InputDeviceOrder.Add(Third);
        config.InputDeviceOrder.Add(Return);

        config.Channels.Add(new ChannelConfig { DeviceId = First, Name = "Mayor 180 degrees", ParticipatesInAutomix = true });
        config.Channels.Add(new ChannelConfig { DeviceId = Second, Name = "Lectern", ParticipatesInAutomix = true });
        config.Channels.Add(new ChannelConfig { DeviceId = Third, Name = "Jabra", ParticipatesInAutomix = true });

        // Not a microphone in the room, so it does not take part - including it means sharing gain
        // with a loudspeaker playing back what was just sent to it.
        config.Channels.Add(new ChannelConfig { DeviceId = Return, Name = "Online return" });

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(First, 1);
        console.AddDevice(Second, 1);
        console.AddDevice(Third, 1);
        console.AddDevice(Return, 1);

        for (int channel = 0; channel < 4; channel++)
        {
            console.Controller.Submit(GraphCommand.SetSend(channel, 0, isOn: true, decibels: 0));
        }

        console.Controller.Pump();

        return console;
    }
}
