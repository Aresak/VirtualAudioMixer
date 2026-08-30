using Vam.Engine.Devices;
using Vam.TestKit.Allocations;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-018. What the strip header and the diagnostics column read, and what it costs to read it.
/// </summary>
public class DeviceTelemetryTests
{
    const int BlockFrames = 120;
    const int TargetFillFrames = 1024;
    const int MaximumDevices = 4;

    static readonly TimeSpan CorrectionInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void PollingEveryDeviceAllocatesNothing()
    {
        using DriftSimulation simulation = CreateSimulation();

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 37.0);
        simulation.AddDevice("Jabra", driftPpm: -22.0);
        simulation.Run(TimeSpan.FromSeconds(5));

        (DeviceInputChannelRegistry Registry, DeviceTelemetry[] Buffer) state =
            (simulation.Registry, new DeviceTelemetry[MaximumDevices]);

        // The caller owns the buffer, so a poll at meter rate costs nothing for the collector to
        // clean up afterwards. Returning a fresh list here would be the only allocation in the whole
        // telemetry path and it would be in the hot part of it.
        AllocationAssert.None(state, static work => work.Registry.GetAllTelemetry(work.Buffer));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AShortSpanIsRefusedRatherThanReportingFewerDevices()
    {
        using DriftSimulation simulation = CreateSimulation();

        simulation.AddDevice("First", driftPpm: 0.0);
        simulation.AddDevice("Second", driftPpm: 0.0);

        DeviceTelemetry[] tooSmall = new DeviceTelemetry[1];

        // Quietly filling what fits would show one device on a console that has two, which is worse
        // than an exception a developer sees once.
        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.Registry.GetAllTelemetry(tooSmall));
    }

    [Theory]
    [InlineData(45.0)]
    [InlineData(-45.0)]
    [Trait("Category", TestCategories.Unit)]
    public void TheReportedRateIsTheRateTheDeviceIsReallyRunningAt(double driftPpm)
    {
        using DriftSimulation simulation = CreateSimulation();

        simulation.AddDevice("Mayor 180 degrees", driftPpm);
        simulation.Prime();
        simulation.Run(TimeSpan.FromMinutes(6));

        DeviceTelemetry[] buffer = new DeviceTelemetry[MaximumDevices];
        int written = simulation.Registry.GetAllTelemetry(buffer);

        Assert.Equal(1, written);

        // Judged against what the device is actually doing rather than against the estimator, which
        // is the whole difficulty: once the servo holds the fill flat there is no slope left for a
        // fill-based estimate to read, and a channel that reported the estimator's number directly
        // would show the nominal rate for a device running forty-five parts per million fast.
        Assert.Equal(simulation.EffectiveSampleRateOf(0), buffer[0].MeasuredSampleRate, 0.15);
        Assert.Equal(driftPpm, buffer[0].DriftPpm, 3.0);

        Assert.Equal(DriftSimulation.NominalSampleRate, buffer[0].NominalSampleRate);
        Assert.InRange(buffer[0].FillPercentage, 0.0, 100.0);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheAudioPathStillAllocatesNothingWhileTelemetryIsBeingPolled()
    {
        using DriftSimulation simulation = CreateSimulation();

        simulation.AddDevice("Mayor 180 degrees", driftPpm: 37.0);
        simulation.Prime();

        using CancellationTokenSource finished = new();

        Thread poller = new(() => PollUntilCancelled(simulation.Registry, finished.Token))
        {
            IsBackground = true,
            Name = "telemetry-poller"
        };

        poller.Start();

        try
        {
            (DeviceInputChannel Channel, float[] Input, float[] Output) state =
                (simulation.Channels[0], new float[BlockFrames], new float[BlockFrames]);

            // The busy test, and it is the one that earns its keep. A device name formatted into a
            // string, or a lambda that captures, passes the quiet measurement and fails this one -
            // and this one is what a live session looks like, because somebody is reading the meters.
            AllocationAssert.None(
                state,
                static work =>
                {
                    work.Channel.Write(work.Input, BlockFrames);
                    work.Channel.Pull(work.Output);
                });
        }
        finally
        {
            finished.Cancel();
            poller.Join(TimeSpan.FromSeconds(5));
        }
    }

    static DriftSimulation CreateSimulation() => new(BlockFrames, TargetFillFrames, CorrectionInterval);

    static void PollUntilCancelled(DeviceInputChannelRegistry registry, CancellationToken cancellationToken)
    {
        DeviceTelemetry[] buffer = new DeviceTelemetry[MaximumDevices];

        while (!cancellationToken.IsCancellationRequested)
        {
            registry.GetAllTelemetry(buffer);
        }
    }
}
