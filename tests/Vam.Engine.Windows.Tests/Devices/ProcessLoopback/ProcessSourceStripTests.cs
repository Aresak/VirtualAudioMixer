using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>
/// #55. A strip on a <c>process:</c> source goes unavailable when the browser closes and recovers
/// when it is back, through the same supervisor path an unplugged microphone takes.
/// </summary>
/// <remarks>
/// <see cref="NullAudioBackend"/> stands in for the Windows one: the point is that nothing above the
/// backend needs to know the identity is a process.
/// </remarks>
public class ProcessSourceStripTests
{
    const int BlockFrames = 120;

    static readonly AudioDeviceId Meeting = new("process:msedge.exe");
    static readonly TimeSpan PastReconcile = TimeSpan.FromSeconds(6);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABrowserNotYetRunningLeavesTheStripUnavailableUntilItStarts()
    {
        using NullAudioBackend backend = new();
        using Fixture fixture = new(backend);

        DeviceInputChannel channel = fixture.Track(Meeting);

        Assert.Equal(DeviceStreamState.Absent, channel.State);

        backend.AddDevice(Meeting, DeviceDirection.Capture, new NullDeviceOptions("msedge.exe"));
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Running, channel.State);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingTheBrowserMarksTheStripUnavailableAndReopeningRecoversIt()
    {
        using NullAudioBackend backend = new();
        backend.AddDevice(Meeting, DeviceDirection.Capture, new NullDeviceOptions("msedge.exe"));

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(Meeting);

        Assert.Equal(DeviceStreamState.Running, channel.State);

        // No notification: a process exit only shows up as the source missing from the listing.
        backend.RemoveDevice(Meeting);
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Absent, channel.State);
        Assert.Contains(fixture.Changes, change => change.Kind == DeviceChangeKind.Removed && change.DeviceId == Meeting);

        backend.AddDevice(Meeting, DeviceDirection.Capture, new NullDeviceOptions("msedge.exe"));
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(2, backend.CaptureStreams.Count);
        Assert.Equal(DeviceStreamState.Running, backend.CaptureStreams[^1].State);
        Assert.NotEqual(DeviceStreamState.Running, backend.CaptureStreams[0].State);
    }

    /// <summary>Wires a supervisor to a backend and collects what it announces.</summary>
    sealed class Fixture : IDisposable
    {
        readonly List<DeviceChange> changes = [];
        readonly RecordingLoggerFactory loggers = new();
        readonly DeviceSupervisor supervisor;

        public Fixture(NullAudioBackend backend)
        {
            supervisor = new DeviceSupervisor(backend, new DeviceInputChannelRegistry(), loggers);
            supervisor.Changed += (_, change) => changes.Add(change);
        }

        public IReadOnlyList<DeviceChange> Changes => changes;

        public DeviceInputChannel Track(AudioDeviceId deviceId) =>
            supervisor.Track(
                deviceId,
                new DeviceInputChannelOptions
                {
                    NominalSampleRate = 48000,
                    ChannelCount = 2,
                    BlockFrames = BlockFrames,
                    RingCapacityFrames = 4096,
                    TargetFillFrames = 1024
                },
                new CaptureOptions(ShareMode.Shared, TimeSpan.FromMilliseconds(20), ChannelCount: 2, SampleRate: 48000));

        /// <summary>Runs the control loop for a while, with the mix pulling the strip throughout.</summary>
        public void PullThrough(DeviceInputChannel channel, TimeSpan duration)
        {
            float[] block = new float[BlockFrames * 2];
            TimeSpan tick = TimeSpan.FromMilliseconds(100);

            for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += tick)
            {
                channel.Pull(block);
                supervisor.Poll(tick);
            }
        }

        public void Dispose()
        {
            supervisor.Dispose();
            loggers.Dispose();
        }
    }
}
