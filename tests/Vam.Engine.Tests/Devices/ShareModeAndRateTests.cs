using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// The decision of 2026-09-16: exclusive mode only where the device's own format already runs at
/// the engine's rate, and the engine gets the rate it asked for or the device stays closed.
/// </summary>
/// <remarks>
/// <para>
/// The device that motivated all of it is a conference speakerphone whose own converter runs at
/// 16 kHz behind a 48 kHz mix format, and it is here as <see cref="Speakerphone"/>. What cannot be
/// checked here is whether WASAPI behaves the way the fake does; that lives in
/// <c>Vam.Engine.Windows.Tests</c> behind the hardware gate, and is owed.
/// </para>
/// <para>
/// The refusal cases matter more than the acceptance ones. Before this, a device granted at a rate
/// the strip was not built for was fed into the ring anyway, and the result is not a degraded
/// session - it is a ring that drains for four hours against a servo with five hundred parts per
/// million of authority.
/// </para>
/// </remarks>
public class ShareModeAndRateTests
{
    const int MixRateHz = 48000;
    const int SpeakerphoneRateHz = 16000;
    const int BlockFrames = 120;
    const int TargetFillFrames = 1024;

    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceAtTheMixRateIsTakenExclusivelyWhenExclusiveIsAskedFor()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, Lectern);

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id, ShareMode.Exclusive);

        DeviceTelemetry telemetry = channel.GetTelemetry();

        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(ShareMode.Exclusive, telemetry.ShareMode);
        Assert.False(telemetry.IsSystemConverting);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASpeakerphoneIsOpenedSharedEvenWhenExclusiveIsAskedFor()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, Speakerphone);

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id, ShareMode.Exclusive);

        DeviceTelemetry telemetry = channel.GetTelemetry();

        // The whole decision in three assertions. Exclusive would have granted 16 kHz, which is a
        // ratio of three and a hundred and eighty times what the drift resampler may do.
        Assert.Equal(DeviceStreamState.Running, channel.State);
        Assert.Equal(ShareMode.Shared, telemetry.ShareMode);
        Assert.True(telemetry.IsSystemConverting);
        Assert.Equal(SpeakerphoneRateHz, telemetry.DeviceSampleRate);
        Assert.Equal(MixRateHz, telemetry.NominalSampleRate);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceReportsItsOwnFormatSeparatelyFromTheOneItPresents()
    {
        using NullAudioBackend backend = new();

        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, Speakerphone);

        // The two questions the old code asked as one. The mix format is what shared mode presents;
        // the native format is what exclusive mode would deliver, and they are not the same device
        // fact.
        Assert.Equal(MixRateHz, device.NominalSampleRate);
        Assert.Equal(SpeakerphoneRateHz, device.NativeSampleRate);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatCannotBeGivenTheMixRateIsLeftClosed()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, AsioLikeSpeakerphone);

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id, ShareMode.Shared);

        // Silent and retrying beats open and wrong. Nothing converts 16 kHz to 48 kHz here, and a
        // ring fed at a third of the rate it is drained at is a session-long underrun.
        Assert.Equal(DeviceStreamState.Absent, channel.State);
        Assert.Empty(backend.CaptureStreams);
        Assert.Contains(fixture.Changes, change => change.Kind == DeviceChangeKind.OpenFailed);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStreamGrantedAtAnotherRateIsRefusedRatherThanFedIntoTheRing()
    {
        using NullAudioBackend inner = new();
        AudioDeviceInfo device = inner.AddDevice(DeviceDirection.Capture, Lectern);

        using DishonestBackend backend = new(inner, MixRateHz / 2);
        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id, ShareMode.Shared);

        Assert.Equal(DeviceStreamState.Absent, channel.State);
        Assert.Contains(fixture.Changes, change => change.Kind == DeviceChangeKind.OpenFailed);

        // Disposed on the way out rather than left running into a ring nobody reads.
        Assert.All(inner.CaptureStreams, stream => Assert.NotEqual(DeviceStreamState.Running, stream.State));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatGoesAwayStopsClaimingAShareMode()
    {
        using NullAudioBackend backend = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, Speakerphone);

        using Fixture fixture = new(backend);
        DeviceInputChannel channel = fixture.Track(device.Id, ShareMode.Shared);

        Assert.True(channel.GetTelemetry().IsSystemConverting);

        backend.RemoveDevice(device.Id);
        fixture.Remove(device.Id);

        // A strip saying "Windows is converting this" for a microphone that is not plugged in is a
        // claim somebody would act on.
        Assert.Equal(0, channel.GetTelemetry().DeviceSampleRate);
        Assert.False(channel.GetTelemetry().IsSystemConverting);
    }

    static NullDeviceOptions Lectern =>
        new("Lectern", ChannelCount: 1, NominalSampleRate: MixRateHz);

    static NullDeviceOptions Speakerphone =>
        new(
            "Jabra SPEAK 510",
            ChannelCount: 1,
            NominalSampleRate: MixRateHz,
            NativeSampleRate: SpeakerphoneRateHz,
            NativeChannelCount: 1);

    static NullDeviceOptions AsioLikeSpeakerphone => Speakerphone with { CanConvertRate = false };

    /// <summary>Wires a supervisor to a backend and collects what it announces.</summary>
    sealed class Fixture : IDisposable
    {
        readonly List<DeviceChange> changes = [];
        readonly RecordingLoggerFactory loggers = new();
        readonly DeviceSupervisor supervisor;

        public Fixture(IAudioBackend backend)
        {
            supervisor = new DeviceSupervisor(backend, new DeviceInputChannelRegistry(), loggers);
            supervisor.Changed += (_, change) => changes.Add(change);
        }

        public IReadOnlyList<DeviceChange> Changes => changes;

        public DeviceInputChannel Track(AudioDeviceId deviceId, ShareMode shareMode) =>
            supervisor.Track(
                deviceId,
                new DeviceInputChannelOptions
                {
                    NominalSampleRate = MixRateHz,
                    ChannelCount = 1,
                    BlockFrames = BlockFrames,
                    RingCapacityFrames = 4096,
                    TargetFillFrames = TargetFillFrames
                },
                new CaptureOptions(shareMode, TimeSpan.FromMilliseconds(20), 1, MixRateHz));

        public void Remove(AudioDeviceId deviceId)
        {
            supervisor.Post(new DeviceChange(DeviceChangeKind.Removed, deviceId, deviceId.Value, DateTimeOffset.UtcNow));
            supervisor.Poll(Tick);
        }

        public void Dispose()
        {
            supervisor.Dispose();
            loggers.Dispose();
        }
    }

    /// <summary>
    /// A backend that opens a stream and then misreports what it granted.
    /// </summary>
    /// <remarks>
    /// Nothing real does this, which is the point: the supervisor's check exists because the failure
    /// it catches is completely silent, and a guard nothing can reach is a guard nobody can trust.
    /// </remarks>
    sealed class DishonestBackend(NullAudioBackend inner, int grantedRateHz) : IAudioBackend
    {
        public string Id => inner.Id;

        public bool CanProvideTimebase => inner.CanProvideTimebase;

        public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction) => inner.Enumerate(direction);

        public AudioDeviceInfo? DefaultDevice(DeviceDirection direction) => inner.DefaultDevice(direction);

        public ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options) =>
            new Misreporting(inner.OpenCapture(deviceId, options), grantedRateHz);

        public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options) =>
            inner.OpenRender(deviceId, options);

        public void Dispose() => inner.Dispose();

        sealed class Misreporting(ICaptureStream inner, int rateHz) : ICaptureStream
        {
            public AudioDeviceId DeviceId => inner.DeviceId;

            public DeviceDirection Direction => inner.Direction;

            public AudioStreamFormat Format => inner.Format with { SampleRate = rateHz };

            public DeviceStreamState State => inner.State;

            public void Start(CaptureCallback onSamplesCaptured) => inner.Start(onSamplesCaptured);

            public void Stop() => inner.Stop();

            public void Dispose() => inner.Dispose();
        }
    }
}
