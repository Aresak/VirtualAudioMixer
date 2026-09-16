using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Graph;

/// <summary>
/// B8's pan. Irrelevant for the stream and not irrelevant at all for a monitor.
/// </summary>
public class PanTests
{
    static readonly AudioDeviceId Microphone = new("null:capture:mayor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void CentreIsUnityOnBothSides()
    {
        (float left, float right) = Render(pan: 0);

        // Unity, not 0.707. A mono strip has always been heard at unity across both sides of a
        // stereo bus and pan defaults to centre, so the textbook law would have made every existing
        // console three decibels quieter the moment this feature landed.
        Assert.Equal(left, right, 0.001f);
        Assert.Equal(0.5f, left, 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void HardLeftLeavesNothingOnTheRight()
    {
        (float left, float right) = Render(pan: -1);

        Assert.True(left > 0.65f, $"The left side got {left}.");
        Assert.True(right < 0.001f, $"The right side got {right}, which should have been silence.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void HardRightLeavesNothingOnTheLeft()
    {
        (float left, float right) = Render(pan: 1);

        Assert.True(right > 0.65f, $"The right side got {right}.");
        Assert.True(left < 0.001f, $"The left side got {left}, which should have been silence.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void MovingAStripDoesNotChangeHowLoudItIs()
    {
        // Constant power, not linear. A linear law drops a centred voice by three decibels the moment
        // somebody moves it, the operator corrects that on the fader, and then the two controls are
        // fighting each other for the rest of the meeting.
        foreach (double pan in (double[])[-1, -0.5, 0, 0.5, 1])
        {
            (float left, float right) = Render(pan);
            double power = Math.Sqrt((left * left) + (right * right));

            // Constant across the travel. Where the constant sits is the centre-unity choice above.
            Assert.Equal(0.5 * Math.Sqrt(2), power, 0.01);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMonoBusHearsTheWholeStripWhereverItIsPanned()
    {
        // Nothing to pan into. A mono bus that quietened a hard-panned strip would be turning a
        // monitor preference into a change to what goes out.
        Assert.Equal(Render(pan: -1, busWidth: 1).Left, Render(pan: 0, busWidth: 1).Left, 0.001f);
    }

    static (float Left, float Right) Render(double pan, int busWidth = 2)
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Microphone);
        config.Channels.Add(new ChannelConfig { DeviceId = Microphone, Name = "Mayor 180 degrees", Pan = pan });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Output, ChannelCount = busWidth });
        config.PrimaryOutputChannelCount = busWidth;

        ConsoleFixture console = new(config);

        console.AddDevice(Microphone, 1);

        console.Controller.Submit(GraphCommand.SetSend(0, 0, isOn: true, decibels: 0));
        console.Controller.Pump();

        console.Feed(0, 0.5f);
        console.RenderUntilSettled(busWidth);

        float left = 0f;
        float right = 0f;

        for (int frame = 0; frame < console.Output.Length; frame += busWidth)
        {
            left = Math.Max(left, Math.Abs(console.Output[frame]));

            if (busWidth > 1)
            {
                right = Math.Max(right, Math.Abs(console.Output[frame + 1]));
            }
        }

        return (left, right);
    }
}
