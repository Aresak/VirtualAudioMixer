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

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
