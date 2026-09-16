using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Allocations;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-021. What decides that a block's worth of time has passed, and what happens when the thing
/// that was deciding gets unplugged.
/// </summary>
public class MasterClockTests
{
    const int BlockFrames = 120;
    const int SampleRate = 48000;
    const int TargetFillFrames = 1024;
    const int Blocks = 8;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EveryInputAdvancesInStepWithThePrimaryOutput()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        DeviceInputChannel first = fixture.AddInput("Mayor 180 degrees");
        DeviceInputChannel second = fixture.AddInput("Lectern");

        Fill(first, Blocks * 2);
        Fill(second, Blocks * 2);

        int firstBefore = first.FillFrames;
        int secondBefore = second.FillFrames;

        fixture.StartPrimary("Monitor");
        fixture.PumpBlocks(Blocks);

        // Lockstep is the whole claim: both rings drained by the same amount because both were
        // pulled by the same clock, the same number of times.
        Assert.Equal(firstBefore - first.FillFrames, secondBefore - second.FillFrames);
        Assert.Equal(Blocks, fixture.Clock.BlocksRendered);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheConsumerSeesOneBlockFromEveryDevice()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        DeviceInputChannel first = fixture.AddInput("Mayor 180 degrees");
        fixture.AddInput("Lectern");

        Fill(first, Blocks * 2);

        int seenDevices = -1;
        int seenFrames = -1;

        fixture.Clock.SetConsumer((blocks, output, frameCount) =>
        {
            seenDevices = blocks.Count;
            seenFrames = blocks.FrameCount;
            output.Clear();
            return frameCount;
        });

        fixture.StartPrimary("Monitor");
        fixture.PumpBlocks(1);

        // Every device every block, present or not. A device that is absent contributes silence
        // rather than dropping out of the set, so the graph sees the same shape whatever is plugged in.
        Assert.Equal(2, seenDevices);
        Assert.Equal(BlockFrames, seenFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void LosingThePrimaryPromotesAnotherOutputRatherThanStopping()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        fixture.AddInput("Mayor 180 degrees");

        AudioDeviceInfo monitor = fixture.AddOutput("Monitor");
        AudioDeviceInfo stream = fixture.AddOutput("Stream feed");

        Assert.True(fixture.Clock.SetPrimary(monitor.Id));

        backend.RemoveDevice(monitor.Id);
        fixture.Clock.Poll();

        // Somebody unplugged the monitor headphones. The session carries on, on the other output.
        Assert.Equal(stream.Id, fixture.Clock.PrimaryDeviceId);
        Assert.False(fixture.Clock.IsOnFallbackTimer);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WithNoOutputAtAllTheEngineKeepsRunningOnItsOwnTimer()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        fixture.AddInput("Mayor 180 degrees");

        AudioDeviceInfo monitor = fixture.AddOutput("Monitor");
        Assert.True(fixture.Clock.SetPrimary(monitor.Id));

        backend.RemoveDevice(monitor.Id);
        fixture.Clock.Poll();

        Assert.True(fixture.Clock.IsOnFallbackTimer);
        Assert.True(fixture.Clock.PrimaryDeviceId.IsNone);

        long before = fixture.Clock.BlocksRendered;
        Thread.Sleep(TimeSpan.FromMilliseconds(200));

        // The audio is going nowhere, and it is still being pulled. A council session where the
        // headphones get unplugged must not stop recording, and recording is what makes a bad
        // session recoverable.
        Assert.True(
            fixture.Clock.BlocksRendered > before,
            "The fallback timer did not advance, so the engine stopped when the last output went.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void PullingABlockAllocatesNothing()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        DeviceInputChannel channel = fixture.AddInput("Mayor 180 degrees");

        fixture.Clock.SetConsumer(static (blocks, output, frameCount) =>
        {
            output.Clear();
            return frameCount;
        });

        fixture.StartPrimary("Monitor");

        (NullRenderStream Stream, DeviceInputChannel Channel, float[] Block) state =
            (fixture.Primary, channel, new float[BlockFrames]);

        AllocationAssert.None(state, static work =>
        {
            work.Channel.Write(work.Block, BlockFrames);
            work.Stream.PumpBuffer();
        });
    }

    static void Fill(DeviceInputChannel channel, int blocks)
    {
        float[] block = new float[BlockFrames];

        for (int index = 0; index < blocks; index++)
        {
            channel.Write(block, BlockFrames);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatAsksForSeveralBlocksAtOnceGetsSeveralBlocks()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        DeviceInputChannel input = fixture.AddInput("Lectern");

        Fill(input, Blocks * 4);
        fixture.StartPrimary("Interface");

        int before = input.FillFrames;
        const int Burst = BlockFrames * 4;

        // What WASAPI does before a stream starts: it hands over the whole endpoint buffer, which in
        // shared mode is several blocks. The graph is built for exactly one, and asking it for four
        // at once used to read past its arena and throw - on the audio thread, inside the device's
        // own Start, where the only visible effect was the device being rejected as a clock.
        fixture.Primary.PumpFrames(Burst);

        Assert.Equal(4, fixture.Clock.BlocksRendered);

        // Four blocks of audio really left the rings, rather than one being rendered and the rest
        // being silence the device then played.
        Assert.Equal(Burst, before - input.FillFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ThePreferredDeviceKeepsTimeRatherThanWhicheverEnumeratesFirst()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        AudioDeviceInfo first = fixture.AddOutput("HDMI display");
        AudioDeviceInfo chosen = fixture.AddOutput("Interface");

        fixture.Clock.Preferred = chosen.Id;

        // Promotion order is enumeration order, which is an accident. The primary bus plays out
        // through the clock, so the device somebody chose for it has to be the one keeping time -
        // otherwise the console names one destination while the mix leaves through another.
        Assert.Equal(chosen.Id, fixture.Clock.PrimaryDeviceId);
        Assert.NotEqual(first.Id, fixture.Clock.PrimaryDeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void APreferredDeviceThatGoesAwayIsReplacedAndNotWaitedFor()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        AudioDeviceInfo chosen = fixture.AddOutput("Interface");
        AudioDeviceInfo other = fixture.AddOutput("Speakers");

        fixture.Clock.Preferred = chosen.Id;
        backend.RenderStreams[^1].SimulateRemoval();

        fixture.Clock.Poll();

        // Preference, not a requirement. A session without a timebase is worse than a session on the
        // wrong device, so something else takes over rather than nothing.
        Assert.Equal(other.Id, fixture.Clock.PrimaryDeviceId);

        // And the preference survives it. Forgetting would mean one hiccup silently abandoning the
        // output somebody chose, with nothing said and no way back short of restarting.
        Assert.Equal(chosen.Id, fixture.Clock.Preferred);
    }

    /// <summary>Wires a clock to a backend and a set of inputs.</summary>
    sealed class Fixture : IDisposable
    {
        readonly NullAudioBackend backend;

        public Fixture(NullAudioBackend backend)
        {
            this.backend = backend;

            Channels = new DeviceInputChannelRegistry();

            Clock = new MasterClock(
                backend,
                Channels,
                new MasterClockOptions
                {
                    BlockFrames = BlockFrames,
                    SampleRate = SampleRate,
                    MaxDevices = 8,
                    MaxChannelsPerDevice = 2
                },
                NullLoggerFactory.Instance);
        }

        public DeviceInputChannelRegistry Channels { get; }

        public MasterClock Clock { get; }

        public NullRenderStream Primary => backend.RenderStreams[^1];

        public DeviceInputChannel AddInput(string name)
        {
            AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(name));

            DeviceInputChannel channel = new(
                device.Id,
                new DeviceInputChannelOptions
                {
                    NominalSampleRate = SampleRate,
                    ChannelCount = 1,
                    BlockFrames = BlockFrames,
                    RingCapacityFrames = 4096,
                    TargetFillFrames = TargetFillFrames
                },
                NullLogger<DeviceInputChannel>.Instance);

            Channels.Add(channel);

            return channel;
        }

        public AudioDeviceInfo AddOutput(string name) =>
            backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions(name));

        public void StartPrimary(string name) => Clock.SetPrimary(AddOutput(name).Id);

        public void PumpBlocks(int count)
        {
            for (int index = 0; index < count; index++)
            {
                Primary.PumpBuffer();
            }
        }

        public void Dispose() => Clock.Dispose();
    }
}
