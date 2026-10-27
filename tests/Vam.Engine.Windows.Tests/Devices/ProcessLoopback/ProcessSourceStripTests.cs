using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;
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
/// Drives the supervisor over <see cref="ProcessLoopbackBackend"/> with a scripted process list, so
/// the listing and the lease are the real ones (#104). Only the WASAPI activation is stood in for,
/// and that is left to the manual check with a real browser (#103).
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
        using Fixture fixture = new();

        DeviceInputChannel channel = fixture.Track(Meeting);

        Assert.Equal(DeviceStreamState.Absent, channel.State);

        fixture.Processes.Start(200, 1, "msedge.exe");
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Running, channel.State);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingTheBrowserMarksTheStripUnavailableAndReopeningRecoversIt()
    {
        using Fixture fixture = new();
        fixture.Processes.Start(200, 1, "msedge.exe");

        DeviceInputChannel channel = fixture.Track(Meeting);

        Assert.Equal(DeviceStreamState.Running, channel.State);

        // No notification: a process exit only shows up as the source missing from the listing.
        fixture.Processes.Exit(200);
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Absent, channel.State);
        Assert.Contains(fixture.Changes, change => change.Kind == DeviceChangeKind.Removed && change.DeviceId == Meeting);
        Assert.True(fixture.Opener.Opened[0].IsDisposed);

        fixture.Processes.Start(200, 1, "msedge.exe");
        fixture.PullThrough(channel, PastReconcile);

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(2, fixture.Opener.Opened.Count);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABrowserRestartedUnderANewProcessIdIsCapturedAgain()
    {
        using Fixture fixture = new();
        fixture.Processes.Start(200, 1, "msedge.exe");

        DeviceInputChannel channel = fixture.Track(Meeting);

        // Restarted between two reconciles, so the strip never sees the browser missing by name.
        fixture.Processes.Exit(200);
        fixture.Processes.Start(300, 1, "msedge.exe");
        fixture.PullThrough(channel, PastReconcile + PastReconcile);

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(300, fixture.Opener.Opened[^1].Process.ProcessId);
        Assert.True(fixture.Opener.Opened[0].IsDisposed);
    }

    /// <summary>Wires a supervisor to the process backend and collects what it announces.</summary>
    sealed class Fixture : IDisposable
    {
        readonly List<DeviceChange> changes = [];
        readonly RecordingLoggerFactory loggers = new();
        readonly ProcessLoopbackBackend backend;
        readonly DeviceSupervisor supervisor;

        public Fixture()
        {
            backend = new ProcessLoopbackBackend(
                new NullAudioBackend(),
                Opener,
                new ProcessLoopbackSources(Processes, loggers.CreateLogger("sources")));
            supervisor = new DeviceSupervisor(backend, new DeviceInputChannelRegistry(), loggers);
            supervisor.Changed += (_, change) => changes.Add(change);
        }

        public ScriptedProcessSnapshot Processes { get; } = new();

        public StandInProcessOpener Opener { get; } = new();

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
            backend.Dispose();
            loggers.Dispose();
        }
    }
}
