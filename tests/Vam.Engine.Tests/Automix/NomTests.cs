using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Graph.Nodes;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Automix;

/// <summary>
/// C4. What the automixer adds back as more microphones open.
/// </summary>
/// <remarks>
/// Gain sharing normalises the shares to one, which holds the bus constant only for sources that sum
/// coherently. Four microphones picking up the same voice from different distances do not, and the
/// bus falls by three decibels for every doubling. This is the compensation, and it was computed and
/// displayed for a fortnight before it was ever applied.
/// </remarks>
public class NomTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void OneOpenMicrophoneGetsNothingBack()
    {
        // Nothing to compensate for. One microphone at full share is the case gain sharing already
        // handles exactly, and adding anything here would make a single speaker louder than they
        // were before the automixer was switched on.
        Assert.Equal(0f, Gain(1), 0.001f);
    }

    [Theory]
    [InlineData(2, 3.0)]
    [InlineData(4, 6.0)]
    [InlineData(8, 9.0)]
    [Trait("Category", TestCategories.Unit)]
    public void EachDoublingAddsThreeDecibels(int open, double expected)
    {
        // Ten times the logarithm, not twenty. Twenty would compensate for coherent summing, which
        // is the case that does not need compensating.
        Assert.Equal(expected, Gain(open), 0.05f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ItIsCappedRatherThanRunningAway()
    {
        // Past eight equally-open microphones the room is the problem and not the gain. An uncapped
        // compensation would keep lifting a room full of open microphones until the limiter was
        // doing all the work.
        Assert.Equal(AutomixNode.MaximumNomGainDb, Gain(64), 0.001f);
        Assert.Equal(AutomixNode.MaximumNomGainDb, Gain(256), 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoMicrophonesOnOneVoiceEndUpLouderThanTheyDidWithoutIt()
    {
        // The behaviour the compensation exists for, measured through the real graph rather than
        // through the arithmetic: two strips carrying the same voice at 0.25, sharing the gain.
        //
        // Each takes half the share, so without C4 each is scaled to 0.125 and the pair sums to
        // 0.25. With it each is lifted by three decibels to 0.177 and the pair sums to 0.354 - the
        // square root of two times where it started, which is exactly the three decibels that
        // incoherent summing would otherwise have cost.
        float summed = RenderTwoSharing();

        Assert.True(summed > 0.3f, $"The pair summed to {summed}, which is no better than no compensation.");
        Assert.Equal(0.25 * Math.Sqrt(2), summed, 0.03);
    }

    /// <summary>The compensation for a given number of equally open microphones.</summary>
    /// <remarks>
    /// Driven through the node's own constant rather than a copy of the formula, so a change to the
    /// cap fails here rather than being silently agreed with.
    /// </remarks>
    static float Gain(int open)
    {
        // Equal shares, so each is 1/open and the sum of squares is 1/open.
        double sumOfSquares = 1.0 / open;
        double compensation = Math.Min(10.0 * Math.Log10(1.0 / sumOfSquares), AutomixNode.MaximumNomGainDb);

        return open <= 1 ? 0f : (float)compensation;
    }

    static float RenderTwoSharing()
    {
        AudioDeviceId first = new("null:capture:a");
        AudioDeviceId second = new("null:capture:b");

        GraphConfig config = new() { IsAutomixBypassed = false, AutomixDepthDb = -30 };

        config.InputDeviceOrder.Add(first);
        config.InputDeviceOrder.Add(second);

        config.Channels.Add(new ChannelConfig { DeviceId = first, Name = "Left", ParticipatesInAutomix = true });
        config.Channels.Add(new ChannelConfig { DeviceId = second, Name = "Right", ParticipatesInAutomix = true });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = 1 });
        config.PrimaryOutputChannelCount = 1;

        ConsoleFixture console = new(config);

        console.AddDevice(first, 1);
        console.AddDevice(second, 1);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Submit(GraphCommand.SetSend(1, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.25f);
        console.Feed(1, 0.25f);
        console.RenderUntilSettled(1);

        return console.OutputPeak();
    }
}
