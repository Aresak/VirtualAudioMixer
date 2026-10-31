using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Vam.Ui.Abstractions;
using Vam.Ui.Services;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>The console's device list follows the engine while it is connected. #128.</summary>
public partial class ProtocolGateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ADevicePluggedInAfterConnectingIsListedWithoutReconnecting()
    {
        await using VamSessionClient session = await ConnectConsoleAsync();

        Assert.DoesNotContain(session.Devices, device => device.Name == "Visitor's microphone");

        AudioDeviceInfo plugged = devices.AddDevice(DeviceDirection.Capture, new NullDeviceOptions("Visitor's microphone"));

        await session.RefreshDevicesAsync(Token);

        Assert.Contains(session.Devices, device => device.Id == plugged.Id.Value);
        Assert.Equal(ConnectionState.Connected, session.Connection);

        // And the other way: a device that went away is no longer offered.
        devices.RemoveDevice(plugged.Id);

        await session.RefreshDevicesAsync(Token);

        Assert.DoesNotContain(session.Devices, device => device.Id == plugged.Id.Value);
    }

    async Task<VamSessionClient> ConnectConsoleAsync()
    {
        VamSessionClient session = new(
            new VamSessionOptions { Address = $"http://{channel!.Target}" },
            new EngineStopTests.HeadlessPlatform(),
            NullLogger<VamSessionClient>.Instance);

        await session.ConnectAsync(Token);

        Stopwatch waited = Stopwatch.StartNew();

        while (session.Connection != ConnectionState.Connected && waited.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(50, Token);
        }

        Assert.Equal(ConnectionState.Connected, session.Connection);

        return session;
    }
}
