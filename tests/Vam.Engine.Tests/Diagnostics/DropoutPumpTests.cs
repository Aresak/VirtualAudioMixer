using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Diagnostics;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// #100. A device that really keeps dropping out says so once per interval, with a count, rather
/// than filling the log tail by itself.
/// </summary>
public class DropoutPumpTests
{
    const int BlockFrames = 120;
    const int Dropouts = 1000;

    static readonly DateTimeOffset Start = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AThousandDropoutsInASecondAreTwoLinesWithTheRightCount()
    {
        NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Lectern"));
        DropoutLog log = new(Dropouts);
        RecordingLoggerFactory loggers = new();
        DropoutPump pump = new(log, loggers.CreateTyped<DropoutPump>());
        DeviceInputChannel channel = CreateChannel(device.Id, log);
        float[] block = new float[BlockFrames];

        pump.SetNames([device.FriendlyName]);
        channel.State = DeviceStreamState.Running;

        // Nothing is ever written, so every pull is an underrun - well inside one second.
        for (int pull = 0; pull < Dropouts; pull++)
        {
            channel.Pull(block);
        }

        while (pump.Pump() > 0)
        {
        }

        pump.Flush();

        Assert.Equal(Dropouts, log.TotalRecorded);
        Assert.Equal(1, pump.Reported);
        Assert.Equal(1, pump.Summaries);
        Assert.Equal(Dropouts - 1, pump.Folded);
        Assert.Equal(2, loggers.Entries.Count);
        Assert.True(loggers.Mentions($"{Dropouts - 1} more times"), "The repeat line did not carry the count.");
        Assert.True(loggers.Mentions($"costing {(Dropouts - 1) * BlockFrames} frames"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoDevicesDroppingOutTogetherAreCountedSeparately()
    {
        ManualClock clock = new(Start);
        DropoutLog log = new(4096);
        RecordingLoggerFactory loggers = new();
        DropoutPump pump = new(log, loggers.CreateTyped<DropoutPump>(), clock);

        pump.SetNames(["Mayor", "Speakers"]);

        // Interleaved, as an input underrunning and an output underrunning in step would be. Folding
        // only consecutive repeats would write every one of these.
        for (int block = 0; block < 500; block++)
        {
            log.Record(new DropoutRecord(Start.UtcTicks + block, 0, DropoutKind.CaptureUnderrun, 120, 0f));
            log.Record(new DropoutRecord(Start.UtcTicks + block, 1, DropoutKind.RenderUnderrun, 120, 0f));
        }

        PumpAll(pump);
        pump.Flush();

        Assert.Equal(2, pump.Reported);
        Assert.Equal(2, pump.Summaries);
        Assert.Equal(998, pump.Folded);
        Assert.Equal(2, loggers.CountMentioning("499 more times"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatNeverStopsIsOneLinePerInterval()
    {
        ManualClock clock = new(Start);
        DropoutLog log = new(4096);
        RecordingLoggerFactory loggers = new();
        DropoutPump pump = new(log, loggers.CreateTyped<DropoutPump>(), clock) { Interval = TimeSpan.FromSeconds(10) };

        // A minute of a dropout every 10 ms, pumped every quarter of a second like the control loop.
        for (int tick = 0; tick < 240; tick++)
        {
            for (int block = 0; block < 25; block++)
            {
                long ticks = clock.GetUtcNow().UtcTicks + (block * TimeSpan.TicksPerMillisecond * 10);

                log.Record(new DropoutRecord(ticks, 0, DropoutKind.CaptureUnderrun, 120, 0f));
            }

            clock.Advance(TimeSpan.FromMilliseconds(250));
            PumpAll(pump);
        }

        pump.Flush();

        Assert.Equal(1, pump.Reported);
        Assert.Equal(6000 - 1, pump.Folded);
        Assert.InRange(pump.Summaries, 6, 7);
        Assert.Equal(pump.Reported + pump.Summaries, loggers.Entries.Count);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AfterAQuietIntervalTheNextDropoutHasItsOwnLine()
    {
        ManualClock clock = new(Start);
        DropoutLog log = new(64);
        RecordingLoggerFactory loggers = new();
        DropoutPump pump = new(log, loggers.CreateTyped<DropoutPump>(), clock) { Interval = TimeSpan.FromSeconds(10) };

        log.Record(new DropoutRecord(Start.UtcTicks, 0, DropoutKind.CaptureOverrun, 120, 0f));
        log.Record(new DropoutRecord(Start.UtcTicks, 0, DropoutKind.CaptureOverrun, 120, 0f));
        PumpAll(pump);

        // The count is written when its interval ends, not held until something else happens.
        clock.Advance(TimeSpan.FromSeconds(10));
        PumpAll(pump);

        Assert.Equal(1, pump.Summaries);

        // A whole interval with nothing in it, and the run is over.
        clock.Advance(TimeSpan.FromSeconds(10));
        PumpAll(pump);

        log.Record(new DropoutRecord(clock.GetUtcNow().UtcTicks, 0, DropoutKind.CaptureOverrun, 120, 0f));
        PumpAll(pump);

        Assert.Equal(2, pump.Reported);
        Assert.Equal(1, pump.Folded);
    }

    static void PumpAll(DropoutPump pump)
    {
        while (pump.Pump() > 0)
        {
        }
    }

    static DeviceInputChannel CreateChannel(AudioDeviceId deviceId, DropoutLog log) =>
        new(
            deviceId,
            new DeviceInputChannelOptions
            {
                NominalSampleRate = 48000,
                ChannelCount = 1,
                BlockFrames = BlockFrames,
                RingCapacityFrames = 4096,
                TargetFillFrames = 1024,
                Dropouts = log
            },
            new RecordingLogger<DeviceInputChannel>());

    /// <summary>A clock the test moves by hand.</summary>
    sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }
}
