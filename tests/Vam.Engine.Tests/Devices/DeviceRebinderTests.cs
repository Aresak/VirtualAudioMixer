using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.TestKit.Logging;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// #64. A device Windows gave a new endpoint ID is the same device, and a saved console finds it.
/// </summary>
public class DeviceRebinderTests
{
    static readonly AudioDeviceId OldMicrophone = new("null:Capture:old-trust");
    static readonly AudioDeviceId OldMonitor = new("null:Render:old-monitor");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripFollowsItsDeviceToANewId()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo moved = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        GraphConfig config = ConsoleWith(Strip(OldMicrophone, "Councillor", "Trust USB microphone"));

        int rebound = DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(1, rebound);
        Assert.Equal(moved.Id, config.Channels[0].DeviceId);
        Assert.Equal(moved.Id, Assert.Single(config.InputDeviceOrder));

        // The strip keeps everything an operator set on it. Only its device changed.
        Assert.Equal("Councillor", config.Channels[0].Name);
        Assert.Equal(1, loggers.CountMentioning("new endpoint ID"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AConsoleSavedBeforeDeviceNamesWereKeptUsesTheStripName()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo moved = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        GraphConfig config = ConsoleWith(Strip(OldMicrophone, "Trust USB microphone", deviceName: ""));

        DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(moved.Id, config.Channels[0].DeviceId);
        Assert.Equal("Trust USB microphone", config.Channels[0].DeviceName);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TwoDevicesWithTheNameAreNotGuessedBetween()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        GraphConfig config = ConsoleWith(Strip(OldMicrophone, "Councillor", "Trust USB microphone"));

        int rebound = DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(0, rebound);
        Assert.Equal(OldMicrophone, config.Channels[0].DeviceId);
        Assert.Equal(1, loggers.CountMentioning("rather than guessing"));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ADeviceAnotherStripUsesIsNeverTaken()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo inUse = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        GraphConfig config = ConsoleWith(
            Strip(inUse.Id, "Mayor", "Trust USB microphone"),
            Strip(OldMicrophone, "Councillor", "Trust USB microphone"));

        int rebound = DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(0, rebound);
        Assert.Equal(inUse.Id, config.Channels[0].DeviceId);
        Assert.Equal(OldMicrophone, config.Channels[1].DeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ABusFollowsItsOutputByTheRememberedName()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo moved = backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Jabra SPEAK 510", ChannelCount: 2));
        GraphConfig config = new();

        config.Buses.Add(new BusConfig { Name = "Monitor 1", Role = BusRole.Monitor, OutputDeviceId = OldMonitor, OutputDeviceName = "Jabra SPEAK 510" });
        config.Buses.Add(new BusConfig { Name = "Jabra SPEAK 510", Role = BusRole.Monitor, OutputDeviceId = new AudioDeviceId("null:Render:gone") });

        int rebound = DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(1, rebound);
        Assert.Equal(moved.Id, config.Buses[0].OutputDeviceId);

        // A bus has no device name to fall back on: its own name is a label, never a device.
        Assert.Equal(new AudioDeviceId("null:Render:gone"), config.Buses[1].OutputDeviceId);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void APresentDeviceHasItsNameRemembered()
    {
        using NullAudioBackend backend = new();
        using RecordingLoggerFactory loggers = new();
        AudioDeviceInfo microphone = backend.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        AudioDeviceInfo speaker = backend.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Jabra SPEAK 510", ChannelCount: 2));
        GraphConfig config = ConsoleWith(Strip(microphone.Id, "Councillor", deviceName: ""));

        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, OutputDeviceId = speaker.Id });

        int rebound = DeviceRebinder.Apply(config, backend, loggers.CreateLogger("test"));

        Assert.Equal(0, rebound);
        Assert.Equal("Trust USB microphone", config.Channels[0].DeviceName);
        Assert.Equal("Jabra SPEAK 510", config.Buses[0].OutputDeviceName);
    }

    static ChannelConfig Strip(AudioDeviceId deviceId, string name, string deviceName) =>
        new() { DeviceId = deviceId, Name = name, DeviceName = deviceName };

    static GraphConfig ConsoleWith(params ChannelConfig[] strips)
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
