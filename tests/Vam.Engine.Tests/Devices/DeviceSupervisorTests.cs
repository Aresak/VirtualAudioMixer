using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-019 and VAM-020. A device that goes away and comes back, without the operator touching
/// anything and without taking the rest of the session with it.
/// </summary>
/// <remarks>
/// The physical unplug is `needs-hardware` and stays that way. What is testable here is the whole
/// state machine around it, which is the part that has the bugs: the ordering, the idempotence, the
/// backoff, and whether anything leaks per cycle.
/// </remarks>
public class DeviceSupervisorTests
{
    const int BlockFrames = 120;
    const int TargetFillFrames = 1024;
    const int PlugCycles = 20;

    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan PastReconcile = TimeSpan.FromSeconds(6);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TrackingAPresentDeviceOpensItImmediately()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id);

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Single(backend.CaptureStreams);
        Assert.Contains(fixture.Changes, change => change.Kind == DeviceChangeKind.Arrived);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RemovingOneDeviceLeavesTheOtherRunning()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo first = AddDevice(backend, "Mayor 180 degrees");
        AudioDeviceInfo second = AddDevice(backend, "Lectern");

        using Fixture fixture = new(backend);
        DeviceInputChannel firstChannel = fixture.Track(first.Id);
        DeviceInputChannel secondChannel = fixture.Track(second.Id);

        backend.RemoveDevice(first.Id);
        fixture.Remove(first.Id);

        // The whole point. One microphone re-enumerating must not be able to touch the meeting.
        Assert.Equal(DeviceStreamState.Absent, firstChannel.State);
        Assert.Equal(DeviceStreamState.Running, secondChannel.State);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADepartureIsLoggedAndAnnouncedByName()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        fixture.Track(device.Id);

        backend.RemoveDevice(device.Id);
        fixture.Remove(device.Id);

        // A log line naming an endpoint GUID is complete and useless to the person holding the
        // cable, so the name is carried from when the device was open.
        Assert.True(fixture.Loggers.Mentions("Mayor 180 degrees"));

        DeviceChange departure = Assert.Single(fixture.Changes, change => change.Kind == DeviceChangeKind.Removed);
        Assert.Equal("Mayor 180 degrees", departure.FriendlyName);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AReturningDeviceIsReopenedWithItsBuffersAndDriftEstimateCleared()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id);

        // Fill the ring so there is stale audio to carry across the gap, and something to notice if
        // it survives.
        float[] block = new float[BlockFrames];

        for (int pass = 0; pass < 4; pass++)
        {
            channel.Write(block, BlockFrames);
        }

        Assert.True(channel.FillFrames > 0);

        backend.RemoveDevice(device.Id);
        fixture.Remove(device.Id);

        // Back under the same identity, because it is the same microphone. A fresh identity would
        // be a different device and would prove nothing about re-attachment.
        backend.AddDevice(device.Id, DeviceDirection.Capture, new NullDeviceOptions("Mayor 180 degrees"));
        fixture.Arrive(device.Id);

        // Same strip, same channel object, nothing carried over. The old ring holds audio from
        // before the device left, and playing it out would be a glitch with a timestamp.
        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(0, channel.FillFrames);
        Assert.Equal(1.0, channel.Ratio);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoArrivalsForTheSameDeviceProduceOneStream()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        fixture.Track(device.Id);

        // WASAPI sends duplicates routinely. A second stream on the same device would mean two
        // threads writing into one ring, which the ring's whole design forbids.
        fixture.Arrive(device.Id);
        fixture.Arrive(device.Id);

        Assert.Single(backend.CaptureStreams);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnAbsentDeviceBacksOffRatherThanSpinning()
    {
        using NullAudioBackend backend = new();
        AudioDeviceId missing = new("null:Capture:never-existed");

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(missing);

        Assert.Equal(DeviceStreamState.Absent, channel.State);

        // A flapping device must not be able to spin the control loop. Twenty ticks of a tenth of a
        // second is two seconds, which the backoff schedule turns into a handful of attempts.
        for (int tick = 0; tick < 20; tick++)
        {
            fixture.Supervisor.Poll(Tick);
        }

        Assert.InRange(fixture.Changes.Count(change => change.Kind == DeviceChangeKind.OpenFailed), 1, 6);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMissedNotificationIsCaughtByTheReconcile()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id);

        backend.RemoveDevice(device.Id);

        // Nothing is posted. The operating system dropped the notification, which is the case the
        // poll fallback exists for - otherwise this strip is dead for the rest of the meeting.
        fixture.Supervisor.Poll(PastReconcile);

        Assert.Equal(DeviceStreamState.Absent, channel.State);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwentyPlugCyclesLeaveNothingBehind()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = AddDevice(backend, "Mayor 180 degrees");

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id);

        for (int cycle = 0; cycle < PlugCycles; cycle++)
        {
            backend.RemoveDevice(device.Id);
            fixture.Remove(device.Id);

            backend.AddDevice(device.Id, DeviceDirection.Capture, new NullDeviceOptions("Mayor 180 degrees"));
            fixture.Arrive(device.Id);
        }

        // The count is the test. One stream per cycle plus the original, and every one of them but
        // the last stopped - a stream left running would be a thread and a set of COM objects
        // leaked per unplug, which is exactly what an evening of flapping produces.
        Assert.Equal(PlugCycles + 1, backend.CaptureStreams.Count);

        int running = backend.CaptureStreams.Count(stream => stream.State == DeviceStreamState.Running);

        Assert.Equal(1, running);
        Assert.Equal(DeviceStreamState.Running, channel.State);
    }

    static AudioDeviceInfo AddDevice(NullAudioBackend backend, string name) =>
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(name));

    /// <summary>Wires a supervisor to a backend and collects what it announces.</summary>
    sealed class Fixture : IDisposable
    {
        readonly List<DeviceChange> changes = [];

        public Fixture(NullAudioBackend backend)
        {
            Loggers = new RecordingLoggerFactory();
            Channels = new DeviceInputChannelRegistry();
            Supervisor = new DeviceSupervisor(backend, Channels, Loggers);
            Supervisor.Changed += (_, change) => changes.Add(change);
        }

        public RecordingLoggerFactory Loggers { get; }

        public DeviceInputChannelRegistry Channels { get; }

        public DeviceSupervisor Supervisor { get; }

        public IReadOnlyList<DeviceChange> Changes => changes;

        public DeviceInputChannel Track(AudioDeviceId deviceId) =>
            Supervisor.Track(
                deviceId,
                new DeviceInputChannelOptions
                {
                    NominalSampleRate = 48000,
                    ChannelCount = 1,
                    BlockFrames = BlockFrames,
                    RingCapacityFrames = 4096,
                    TargetFillFrames = TargetFillFrames
                },
                new CaptureOptions(ShareMode.Shared, TimeSpan.FromMilliseconds(20), ChannelCount: 1));

        public void Remove(AudioDeviceId deviceId) => PostAndPoll(DeviceChangeKind.Removed, deviceId);

        public void Arrive(AudioDeviceId deviceId) => PostAndPoll(DeviceChangeKind.Arrived, deviceId);

        public void Dispose()
        {
            Supervisor.Dispose();
            Loggers.Dispose();
        }

        void PostAndPoll(DeviceChangeKind kind, AudioDeviceId deviceId)
        {
            Supervisor.Post(new DeviceChange(kind, deviceId, deviceId.Value, DateTimeOffset.UtcNow));
            Supervisor.Poll(Tick);
        }
    }
}
