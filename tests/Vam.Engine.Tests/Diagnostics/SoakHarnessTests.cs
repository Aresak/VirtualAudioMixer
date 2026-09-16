using Vam.Engine.Devices;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// EPIC-12's I5: the soak harness, and the device loss the supervisor's state machine only sees when
/// something injects it.
/// </summary>
/// <remarks>
/// The graph cannot tell the difference between this and a real device, because the mix thread only
/// ever sees a ring. That is what makes this the engine rather than a simulation of it.
/// </remarks>
public class SoakHarnessTests
{
    const int BlockFrames = 120;
    const int TargetFillFrames = 1024;

    static readonly TimeSpan CorrectionInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceLostMidRunGoesQuietAndTheOthersCarryOn()
    {
        using DriftSimulation simulation = new(BlockFrames, TargetFillFrames, CorrectionInterval);

        DeviceInputChannel first = simulation.AddDevice("Mayor 180 degrees", driftPpm: 30.0);
        DeviceInputChannel second = simulation.AddDevice("Lectern", driftPpm: -30.0);

        simulation.Prime();
        simulation.Run(TimeSpan.FromSeconds(10));

        Assert.True(first.FillFrames > 0);

        simulation.RemoveDevice(0);
        simulation.Run(TimeSpan.FromSeconds(2));

        // The lost device drains to nothing and the other is untouched. One microphone leaving must
        // not be able to reach the rest of the console.
        Assert.False(simulation.IsPresent(0));
        Assert.Equal(0, first.FillFrames);
        Assert.True(second.FillFrames > 0, "The other device stopped when its neighbour was unplugged.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatComesBackConvergesFromScratch()
    {
        using DriftSimulation simulation = new(BlockFrames, TargetFillFrames, CorrectionInterval);

        DeviceInputChannel channel = simulation.AddDevice("Mayor 180 degrees", driftPpm: 60.0);

        simulation.Prime();
        simulation.Run(TimeSpan.FromMinutes(3));

        double settled = channel.Ratio;

        Assert.True(Math.Abs((settled - 1.0) * 1_000_000) > 20, "The correction never found the drift.");

        simulation.RemoveDevice(0);
        simulation.Run(TimeSpan.FromSeconds(2));
        simulation.RestoreDevice(0);

        // Cleared, not carried. The old estimate describes a stream that stopped, and applying it to
        // whatever came back would be correcting for a rate the device may no longer run at.
        Assert.Equal(1.0, channel.Ratio);

        simulation.Run(TimeSpan.FromMinutes(3));

        // And then it finds it again, from nothing.
        Assert.Equal(settled, channel.Ratio, 0.00002);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheSameRunTwiceGivesTheSameAnswer()
    {
        double first = RunOnce();
        double second = RunOnce();

        // Deterministic on purpose. A soak that fails one run in ten gets disabled inside a week,
        // and then nothing is protecting anything.
        Assert.Equal(first, second);

        static double RunOnce()
        {
            using DriftSimulation simulation = new(BlockFrames, TargetFillFrames, CorrectionInterval);

            DeviceInputChannel channel = simulation.AddDevice("Mayor 180 degrees", driftPpm: 45.0);

            simulation.Prime();
            simulation.Run(TimeSpan.FromMinutes(2));

            return channel.Ratio;
        }
    }
}
