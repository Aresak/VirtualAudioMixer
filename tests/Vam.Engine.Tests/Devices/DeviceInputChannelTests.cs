using Microsoft.Extensions.Logging;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Devices.Clock;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-017. The seam where a device's ring, its drift estimate and its resampler become one path
/// the mix graph can pull from.
/// </summary>
public class DeviceInputChannelTests
{
    const int SampleRate = 48000;
    const int BlockFrames = 120;
    const int RingCapacityFrames = 4096;
    const int TargetFillFrames = 1024;

    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStarvedChannelProducesSilenceRatherThanStalling()
    {
        DeviceInputChannel channel = CreateChannel();
        float[] block = new float[BlockFrames];

        Array.Fill(block, 1.0f);

        int produced = channel.Pull(block);

        // The device has delivered nothing. The mix thread cannot wait for it - a full block of
        // silence and a counter is the only answer that keeps the session running.
        Assert.Equal(0, produced);
        Assert.All(block, sample => Assert.Equal(0f, sample));
        Assert.Equal(BlockFrames, channel.GetTelemetry().UnderrunCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AudioSurvivesTheRoundTripWithoutLossOrReordering()
    {
        DeviceInputChannel channel = CreateChannel();
        float[] input = new float[BlockFrames];
        float[] output = new float[BlockFrames];

        // A counter rather than a tone: ugly to listen to and ideal for proving that no frame was
        // lost, duplicated or reordered, because every value says where it came from.
        int written = 0;
        List<float> collected = [];

        for (int pass = 0; pass < 16; pass++)
        {
            for (int frame = 0; frame < BlockFrames; frame++)
            {
                input[frame] = written + frame + 1;
            }

            channel.Write(input, BlockFrames);
            written += BlockFrames;

            channel.Pull(output);
            collected.AddRange(output);
        }

        // The correction has never run, so the ratio is still exactly one and the filter is a
        // pass-through. What comes out is the ramp, offset by the filter's group delay.
        int start = collected.FindIndex(static sample => sample > 0.5f);

        Assert.InRange(start, 0, DriftResampler.Taps);

        for (int index = start; index < collected.Count - BlockFrames; index++)
        {
            Assert.Equal(index - start + 1, collected[index], 0.001);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void PullingAndWritingAllocateNothing()
    {
        DeviceInputChannel channel = CreateChannel();

        // A value tuple rather than a captured variable: a capturing lambda allocates its closure
        // and the harness would then be measuring itself.
        (DeviceInputChannel Channel, float[] Input, float[] Output) state =
            (channel, new float[BlockFrames], new float[BlockFrames]);

        AllocationAssert.None(
            state,
            static work =>
            {
                work.Channel.Write(work.Input, BlockFrames);
                work.Channel.Pull(work.Output);
            });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ACorrectionReachesTheResampler()
    {
        DeviceInputChannel channel = CreateChannel();

        FillRingAbove(channel, TargetFillFrames * 2);

        double ratio = channel.UpdateCorrection(Interval);

        // The ring is over its setpoint, so the channel has to consume faster than nominal to bring
        // it back down. Above one is the only direction that does that.
        Assert.True(ratio > 1.0, $"Expected the ratio to rise above unity, but it was {ratio}.");
        Assert.Equal(ratio, channel.Ratio);
        Assert.Equal(1, channel.CorrectionCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANonsenseFillIsClampedAndReportedOnce()
    {
        RecordingLogger<DeviceInputChannel> logger = new();
        DeviceInputChannel channel = CreateChannel(logger);

        // The ring is empty and stays empty, which no amount of clock drift explains.
        for (int update = 0; update < 200; update++)
        {
            channel.UpdateCorrection(Interval);
        }

        Assert.True(channel.IsClamping);
        Assert.Equal(1, channel.ClampCount);

        // Clamped, not nonsense: whatever the loop asked for, what reached the resampler is inside
        // the range it will accept, so nothing here can throw on a timer thread.
        Assert.InRange(
            channel.Ratio,
            1.0 - DriftResampler.MaxRatioDeviation,
            1.0 + DriftResampler.MaxRatioDeviation);

        LogRecord problem = Assert.Single(logger.Problems);

        Assert.Equal(LogLevel.Warning, problem.Level);
        Assert.Contains("ppm limit", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ResetClearsEverythingTheDeviceLeftBehind()
    {
        DeviceInputChannel channel = CreateChannel();

        FillRingAbove(channel, TargetFillFrames * 2);
        channel.UpdateCorrection(Interval);
        channel.Pull(new float[BlockFrames]);

        channel.Reset();

        // A device that came back has a ring full of audio from before it left, and a correction
        // describing a rate error it may no longer have. Neither may survive the gap.
        Assert.Equal(0, channel.FillFrames);
        Assert.Equal(1.0, channel.Ratio);
        Assert.Equal(0, channel.ClampCount);

        DeviceTelemetry telemetry = channel.GetTelemetry();

        Assert.Equal(0, telemetry.UnderrunCount);
        Assert.Equal(SampleRate, telemetry.MeasuredSampleRate);
        Assert.Equal(0.0, telemetry.DriftPpm);
    }

    static DeviceInputChannel CreateChannel(ILogger<DeviceInputChannel>? logger = null) =>
        new(
            new AudioDeviceId("null:test"),
            new DeviceInputChannelOptions
            {
                NominalSampleRate = SampleRate,
                ChannelCount = 1,
                BlockFrames = BlockFrames,
                RingCapacityFrames = RingCapacityFrames,
                TargetFillFrames = TargetFillFrames
            },
            logger ?? new RecordingLogger<DeviceInputChannel>());

    static void FillRingAbove(DeviceInputChannel channel, int frames)
    {
        float[] block = new float[BlockFrames];

        while (channel.FillFrames < frames)
        {
            channel.Write(block, BlockFrames);
        }
    }
}
