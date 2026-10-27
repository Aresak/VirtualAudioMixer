using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Configuration;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Server.Engine;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// #64. A console saved with a device under one endpoint ID, started with the device under another.
/// </summary>
public sealed class MovedDeviceTests : IDisposable
{
    const string Trust = "Microphone (Trust USB microphone )";
    const string Monitor = "Speakers (Jabra SPEAK 510 USB)";

    static readonly AudioDeviceId SavedTrust = new("{0.0.1.00000000}.{2f5db4e4}");
    static readonly AudioDeviceId SavedMonitor = new("{0.0.0.00000000}.{fcc547ed}");

    readonly NullAudioBackend devices = new();
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-moved-" + Guid.NewGuid().ToString("n"));

    string ConsolePath => Path.Combine(workspace, "console.json");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASavedConsoleBindsToItsDevicesUnderTheirNewIds()
    {
        AudioDeviceInfo microphone = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Trust));
        AudioDeviceInfo monitor = devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions(Monitor, 2));
        SaveConsole();

        using (VamEngine engine = CreateEngine())
        {
            engine.Start();

            Assert.Equal(microphone.Id, engine.Graph!.Config.Channels[0].DeviceId);
            Assert.Equal(DeviceStreamState.Running, engine.Channels.Channels[0].State);
            Assert.Equal(monitor.Id, engine.Graph.Config.Buses[0].OutputDeviceId);
            Assert.Equal(monitor.Id, engine.Clock!.Preferred);

            engine.Stop();
        }

        GraphConfig saved = new ConsoleStore(NullLogger<ConsoleStore>.Instance).Load(ConsolePath);

        Assert.Equal(microphone.Id, saved.Channels[0].DeviceId);
        Assert.Equal(monitor.Id, saved.Buses[0].OutputDeviceId);
        Assert.Equal(Monitor, saved.Buses[0].OutputDeviceName);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnAbsentStripPicksUpItsDeviceWhenItArrivesUnderANewId()
    {
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions(Monitor, 2));
        SaveConsole();

        using VamEngine engine = CreateEngine();
        engine.Start();

        Assert.Equal(DeviceStreamState.Absent, engine.Channels.Channels[0].State);

        AudioDeviceInfo microphone = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions(Trust));
        Stopwatch waited = Stopwatch.StartNew();

        // The control loop looks every five seconds. Generous, because other suites share the machine.
        while (engine.Channels.Channels[0].State != DeviceStreamState.Running && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(50);
        }

        Assert.Equal(DeviceStreamState.Running, engine.Channels.Channels[0].State);
        Assert.Equal(microphone.Id, engine.Channels.Channels[0].DeviceId);
        Assert.Equal(microphone.Id, engine.Graph!.Config.Channels[0].DeviceId);
        Assert.Equal(microphone.Id, engine.Graph.Config.InputDeviceOrder[0]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        devices.Dispose();

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    void SaveConsole()
    {
        // The strip as the 27 October console had it, with only the strip's name. A bus saved before
        // #64 has no device name and cannot be found this way; this one was saved since.
        GraphConfig config = new();

        config.Channels.Add(new ChannelConfig { DeviceId = SavedTrust, Name = Trust });
        config.Buses.Add(new BusConfig
        {
            Name = "Stream",
            Role = BusRole.Stream,
            OutputDeviceId = SavedMonitor,
            OutputDeviceName = Monitor
        });
        config.Sends.Add(new SendConfig(0, 0, IsOn: true, LevelDb: 0));

        new ConsoleStore(NullLogger<ConsoleStore>.Instance).Save(ConsolePath, config);
    }

    VamEngine CreateEngine() =>
        new(
            new EngineOptions
            {
                ConsolePath = ConsolePath,
                PresetPath = Path.Combine(workspace, "presets.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false,
                ExpectedSessionDuration = TimeSpan.FromMinutes(1)
            },
            NullLoggerFactory.Instance,
            devices);
}
