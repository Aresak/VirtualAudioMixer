using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Components;
using Vam.Ui.Localization;
using Vam.Ui.Services;
using Vam.Ui.State;
using Vam.Ui.Views;

namespace Vam.Ui.Views;

/// <summary>The code behind <c>MonitorsView.razor</c>.</summary>
public partial class MonitorsView
{
    IReadOnlyList<BusState> Monitors =>
        Session.Console?.Buses
            .Where(bus => bus.Role.Equals("Monitor", StringComparison.OrdinalIgnoreCase))
            .ToList()
        ?? [];

    IReadOnlyList<ChannelState> Channels => Session.Console?.Channels ?? [];

    static string Colour(ChannelState channel) =>
        string.IsNullOrWhiteSpace(channel.Colour) ? StripPalette.For(channel.Index) : channel.Colour;

    static bool HasOutput(BusState monitor) => monitor.OutputDeviceId.Length > 0;

    string OutputName(BusState monitor) =>
        monitor.OutputDeviceName.Length > 0
            ? monitor.OutputDeviceName
            : monitor.OutputDeviceId.Length > 0 ? monitor.OutputDeviceId : L["bus.none"];

    bool IsExcluded(ChannelState channel, BusState monitor) => monitor.ExcludedChannels.Contains(channel.Index);

    bool IsOn(ChannelState channel, BusState monitor) =>
        Session.Console?.Sends
            .FirstOrDefault(send => send.ChannelIndex == channel.Index && send.BusIndex == monitor.Index)?.State == "On";

    async Task ToggleAsync(ChannelState channel, BusState monitor)
    {
        if (IsExcluded(channel, monitor))
        {
            return;
        }

        // The same command the strip's send button sends. A monitor source is a send, and if this
        // were its own mechanism the two would disagree the first time somebody used both.
        await Session.ApplyAsync(new Command
        {
            SetSend = new SetSend
            {
                ChannelIndex = channel.Index,
                BusIndex = monitor.Index,
                On = !IsOn(channel, monitor),
                Decibels = 0
            }
        });
    }

    IEnumerable<DeviceInfo> Renders =>
        Session.Devices.Where(device => device.Direction.Equals("Render", StringComparison.OrdinalIgnoreCase));

    async Task SetOutputAsync(BusState monitor, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetBusOutputDevice = new SetBusOutputDevice
            {
                BusIndex = monitor.Index,
                DeviceId = arguments.Value?.ToString() ?? string.Empty
            }
        });

    async Task AddAsync() =>
        await Session.ApplyAsync(new Command
        {
            AddBus = new AddBus
            {
                Name = L.Format("monitors.newName", Monitors.Count + 1),
                Role = "Monitor",
                ChannelCount = 2,
                OutputDeviceId = string.Empty
            }
        });

    async Task RemoveAsync(BusState monitor) =>
        await Session.ApplyAsync(new Command { RemoveBus = new RemoveBus { BusIndex = monitor.Index } });
}
