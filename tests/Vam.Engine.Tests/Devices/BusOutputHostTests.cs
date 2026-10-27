using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Diagnostics;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// #63. A bus whose device goes away keeps mixing, and that is not a stream of dropouts.
/// </summary>
public class BusOutputHostTests
{
    const int BlockFrames = 120;
    const int ChannelCount = 2;

    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ALostOutputDeviceCostsNoDropouts()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        using BusOutputHost host = new(backend, loggers);
        DropoutLog dropouts = new();

        AudioDeviceInfo device = backend.AddDevice(
            DeviceDirection.Render,
            new NullDeviceOptions("Monitor headphones", ChannelCount));

        host.Reconcile([new BusOutputRequest(1, device.Id, ChannelCount)], CreateOptions(dropouts));

        BusOutputChannel channel = host.ChannelOf(1)!;
        float[] block = new float[BlockFrames * ChannelCount];

        backend.RenderStreams[0].SimulateRemoval();
        host.UpdateCorrections(Interval);

        // Far more than the ring holds. Nothing drains it any more, and every block past full used
        // to be an overrun.
        for (int pass = 0; pass < 400; pass++)
        {
            channel.WriteBlock(block, BlockFrames);
        }

        for (int update = 0; update < 20; update++)
        {
            host.UpdateCorrections(Interval);
        }

        Assert.Equal(DeviceStreamState.Absent, channel.State);
        Assert.Equal(0, dropouts.TotalRecorded);
        Assert.Equal(0, loggers.CountMentioning("ppm limit"));
        Assert.Equal(1, loggers.CountMentioning("lost"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARunningOutputThatIsNotDrainedStillCounts()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        using BusOutputHost host = new(backend, loggers);
        DropoutLog dropouts = new();

        AudioDeviceInfo device = backend.AddDevice(
            DeviceDirection.Render,
            new NullDeviceOptions("Monitor headphones", ChannelCount));

        host.Reconcile([new BusOutputRequest(1, device.Id, ChannelCount)], CreateOptions(dropouts));
        host.UpdateCorrections(Interval);

        BusOutputChannel channel = host.ChannelOf(1)!;
        float[] block = new float[BlockFrames * ChannelCount];

        for (int pass = 0; pass < 400; pass++)
        {
            channel.WriteBlock(block, BlockFrames);
        }

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.True(dropouts.TotalRecorded > 0);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnOutputThatOpensIsPrimedBeforeItsServoRuns()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        using BusOutputHost host = new(backend, loggers);
        DropoutLog dropouts = new();

        AudioDeviceInfo device = backend.AddDevice(
            DeviceDirection.Render,
            new NullDeviceOptions("Monitor headphones", ChannelCount));

        host.Reconcile([new BusOutputRequest(1, device.Id, ChannelCount)], CreateOptions(dropouts));

        BusOutputChannel channel = host.ChannelOf(1)!;
        NullRenderStream stream = backend.RenderStreams[0];
        float[] block = new float[BlockFrames * ChannelCount];

        Assert.True(channel.IsPriming);

        // Three seconds of the mix writing a block and the device draining one, with a correction
        // every quarter of a second.
        for (int tick = 1; tick <= 1200; tick++)
        {
            channel.WriteBlock(block, BlockFrames);
            stream.Pump(TimeSpan.FromSeconds(BlockFrames / 48000.0));

            if (tick % 100 == 0)
            {
                host.UpdateCorrections(Interval);
            }
        }

        Assert.False(channel.IsPriming);
        Assert.Equal(0, loggers.CountMentioning("ppm limit"));
        Assert.Equal(0, dropouts.TotalRecorded);
    }

    static DeviceInputChannelOptions CreateOptions(DropoutLog dropouts) =>
        new()
        {
            NominalSampleRate = 48000,
            ChannelCount = ChannelCount,
            BlockFrames = BlockFrames,
            RingCapacityFrames = 4096,
            TargetFillFrames = 1024,
            Dropouts = dropouts
        };
}
