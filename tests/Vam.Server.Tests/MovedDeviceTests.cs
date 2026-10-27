using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Configuration;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Server.Engine;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>
/// #64. A console saved against devices that Windows has since given new endpoint IDs comes up
/// with every device bound, and saves the new IDs.
/// </summary>
public sealed class MovedDeviceTests : IDisposable
{
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-moved-" + Guid.NewGuid().ToString("n"));

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AConsoleSavedUnderOldIdsOpensTheSameDevicesUnderNewOnes()
    {
        using NullAudioBackend devices = new();

        // Listed first, so a clock that fell back to whatever it found would land here and not on
        // the bus's own device.
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Display audio", ChannelCount: 2));

        AudioDeviceInfo microphone = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));
        AudioDeviceInfo speaker = devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Jabra SPEAK 510", ChannelCount: 2));

        string consolePath = Path.Combine(workspace, "console.json");
        ConsoleStore store = new(NullLogger<ConsoleStore>.Instance);

        GraphConfig saved = new();

        saved.Channels.Add(new ChannelConfig
        {
            DeviceId = new AudioDeviceId("null:Capture:old-trust"),
            Name = "Councillor",
            DeviceName = "Trust USB microphone"
        });

        saved.Buses.Add(new BusConfig
        {
            Name = "Stream",
            Role = BusRole.Stream,
            ChannelCount = 2,
            OutputDeviceId = new AudioDeviceId("null:Render:old-jabra"),
            OutputDeviceName = "Jabra SPEAK 510"
        });

        saved.Sends.Add(new SendConfig(0, 0, IsOn: true, LevelDb: 0));
        store.Save(consolePath, saved);

        using (VamEngine engine = new(
                   new EngineOptions
                   {
                       ConsolePath = consolePath,
                       RecordingDirectory = Path.Combine(workspace, "recordings"),
                       RecordAutomatically = false
                   },
                   NullLoggerFactory.Instance,
                   devices))
        {
            engine.Start();

            Assert.Equal(DeviceStreamState.Running, engine.Channels.Channels[0].State);
            Assert.Equal(speaker.Id, engine.Clock!.PrimaryDeviceId);

            engine.SaveConsole();
        }

        GraphConfig reloaded = store.Load(consolePath);

        Assert.Equal(microphone.Id, reloaded.Channels[0].DeviceId);
        Assert.Equal("Councillor", reloaded.Channels[0].Name);
        Assert.Equal(speaker.Id, reloaded.Buses[0].OutputDeviceId);
        Assert.Equal("Jabra SPEAK 510", reloaded.Buses[0].OutputDeviceName);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStripPicksUpItsDeviceUnderANewIdWhileRunning()
    {
        using NullAudioBackend devices = new();
        devices.AddDevice(DeviceDirection.Render, new NullDeviceOptions("Speakers", ChannelCount: 2));

        string consolePath = Path.Combine(workspace, "console.json");
        ConsoleStore store = new(NullLogger<ConsoleStore>.Instance);
        AudioDeviceId old = new("null:Capture:before-the-port-changed");

        GraphConfig saved = new();

        saved.Channels.Add(new ChannelConfig { DeviceId = old, Name = "Councillor", DeviceName = "Trust USB microphone" });
        saved.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, ChannelCount = 2 });
        saved.Sends.Add(new SendConfig(0, 0, IsOn: true, LevelDb: 0));
        store.Save(consolePath, saved);

        using VamEngine engine = new(
            new EngineOptions
            {
                ConsolePath = consolePath,
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                RecordAutomatically = false
            },
            NullLoggerFactory.Instance,
            devices);

        engine.Start();

        Assert.Equal(DeviceStreamState.Absent, engine.Channels.Channels[0].State);

        // Plugged into another port while the engine runs. The supervisor's reconcile notices
        // within its five-second interval.
        AudioDeviceInfo moved = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Trust USB microphone"));

        Assert.True(
            SpinWait.SpinUntil(() => engine.Channels.Channels[0].State == DeviceStreamState.Running, TimeSpan.FromSeconds(15)),
            "The strip never picked up its device under the new ID.");

        // The graph has to find the strip's input under the new ID, or the strip is silent.
        GraphConfig live = engine.Graph!.Config;

        Assert.Equal(moved.Id, live.Channels[0].DeviceId);
        Assert.Equal(moved.Id, Assert.Single(live.InputDeviceOrder));

        engine.SaveConsole();

        Assert.Equal(moved.Id, store.Load(consolePath).Channels[0].DeviceId);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
