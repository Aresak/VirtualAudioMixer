using Vam.Engine.Configuration;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Configuration;

/// <summary>
/// #64. A USB device moved to another port comes back under a new endpoint ID, and is found again by
/// its name.
/// </summary>
public class MovedDeviceFinderTests
{
    const string Trust = "Microphone (Trust USB microphone )";
    const string Jabra = "Echo Cancelling Speakerphone (Jabra SPEAK 510 USB)";

    static readonly AudioDeviceId SavedTrust = new("{0.0.1.00000000}.{2f5db4e4}");
    static readonly AudioDeviceId SavedJabra = new("{0.0.1.00000000}.{221b0797}");
    static readonly AudioDeviceId SavedStream = new("{0.0.0.00000000}.{aedec451}");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripFindsItsDeviceUnderANewIdAndSaysSoOnce()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Trust));
        GraphConfig config = Console(Strip(SavedTrust, Trust));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        IReadOnlyList<MovedDevice> moves = finder.Follow(config, backend);
        finder.Follow(config, backend);

        Assert.Equal([new MovedDevice(SavedTrust, device.Id, DeviceDirection.Capture, Trust)], moves);
        Assert.Equal(device.Id, config.Channels[0].DeviceId);
        Assert.Equal(device.Id, config.InputDeviceOrder[0]);
        Assert.Equal(Trust, config.Channels[0].DeviceName);
        Assert.Equal(1, loggers.CountMentioning(SavedTrust.Value));
        Assert.True(loggers.Mentions(device.Id.Value));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoDevicesByTheSameNameLeaveTheStripAbsent()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        GraphConfig config = Console(Strip(SavedJabra, Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        finder.Follow(config, backend);
        IReadOnlyList<MovedDevice> moves = finder.Follow(config, backend);

        Assert.Empty(moves);
        Assert.Equal(SavedJabra, config.Channels[0].DeviceId);
        Assert.Single(loggers.Problems);
        Assert.Contains("stays absent", loggers.Problems[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceBoundElsewhereIsNeverTaken()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo bound = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        GraphConfig config = Console(Strip(bound.Id, Jabra), Strip(SavedJabra, Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        IReadOnlyList<MovedDevice> moves = finder.Follow(config, backend);

        Assert.Empty(moves);
        Assert.Equal(bound.Id, config.Channels[0].DeviceId);
        Assert.Equal(SavedJabra, config.Channels[1].DeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceStillOpenForARemovedStripIsNeverTaken()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo open = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        GraphConfig config = Console(Strip(SavedJabra, Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        Assert.Empty(finder.Follow(config, backend, [open.Id]));
        Assert.Equal(SavedJabra, config.Channels[0].DeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoAbsentStripsDoNotRaceForOneDevice()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        GraphConfig config = Console(Strip(SavedJabra, Jabra), Strip(new AudioDeviceId("{0.0.1.00000000}.{other}"), Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        Assert.Empty(finder.Follow(config, backend));
        Assert.Equal(SavedJabra, config.Channels[0].DeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABusOutputFindsItsDeviceByTheNameItLastHad()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo original = backend.AddDevice(SavedStream, DeviceDirection.Render, new NullDeviceOptions(Jabra, 2));
        GraphConfig config = Console();
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, OutputDeviceId = original.Id });
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        // Seen once while present, which is how its name is learnt.
        finder.Follow(config, backend);
        backend.RemoveDevice(original.Id);
        AudioDeviceInfo moved = backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions(Jabra, 2));

        IReadOnlyList<MovedDevice> moves = finder.Follow(config, backend);

        Assert.Single(moves);
        Assert.Equal(moved.Id, config.Buses[0].OutputDeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARenamedStripIsFoundByItsDevicesName()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo device = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Trust));
        GraphConfig config = Console(Strip(SavedTrust, Trust) with { Name = "Mayor", DeviceName = Trust });
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        finder.Follow(config, backend);

        Assert.Equal(device.Id, config.Channels[0].DeviceId);
        Assert.Equal("Mayor", config.Channels[0].Name);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARenderDeviceIsNotTakenForAStrip()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions(Jabra, 2));
        GraphConfig config = Console(Strip(SavedJabra, Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        Assert.Empty(finder.Follow(config, backend));
        Assert.Empty(loggers.Problems);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASecondAmbiguousAbsenceIsReportedAgain()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        GraphConfig config = Console(Strip(SavedJabra, Jabra));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        finder.Follow(config, backend);

        // Back under its old ID for a while, then gone again with the same two look-alikes present.
        backend.AddDevice(SavedJabra, DeviceDirection.Capture, new NullDeviceOptions(Jabra));
        finder.Follow(config, backend);
        backend.RemoveDevice(SavedJabra);
        finder.Follow(config, backend);

        Assert.Equal(2, loggers.Problems.Count);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceThatIsPresentButWillNotOpenIsNotMovedToAnIdenticalSpare()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();

        // Present under its own ID and refusing to open, the way a device held exclusively by
        // another program does. The spare beside it is the same model.
        AudioDeviceInfo stubborn = backend.AddDevice(
            DeviceDirection.Capture,
            new NullDeviceOptions(Trust, NominalSampleRate: 44100, CanConvertRate: false));
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Trust));
        GraphConfig config = Console(Strip(stubborn.Id, Trust));
        MovedDeviceFinder finder = new(loggers.CreateTyped<MovedDeviceFinder>());

        Assert.Empty(finder.Follow(config, backend));
        Assert.Equal(stubborn.Id, config.Channels[0].DeviceId);
    }

    static ChannelConfig Strip(AudioDeviceId deviceId, string name) => new() { DeviceId = deviceId, Name = name };

    static GraphConfig Console(params ChannelConfig[] strips)
    {
        GraphConfig config = new();

        foreach (ChannelConfig strip in strips)
        {
            config.Channels.Add(strip);
            config.InputDeviceOrder.Add(strip.DeviceId);
        }

        return config;
    }
}
