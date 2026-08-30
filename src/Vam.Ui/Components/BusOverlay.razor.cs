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

namespace Vam.Ui.Components;

/// <summary>The code behind <c>BusOverlay.razor</c>.</summary>
public partial class BusOverlay
{
    BusState? Bus =>
        Session.Console is { } console && Shell.SelectedBus >= 0 && Shell.SelectedBus < console.Buses.Count
            ? console.Buses[Shell.SelectedBus]
            : null;

    string NameOf(int channelIndex) =>
        Session.Console is { } console && channelIndex < console.Channels.Count
            ? console.Channels[channelIndex].Name
            : $"#{channelIndex}";

    string OutputName(BusState bus)
    {
        foreach (DeviceInfo device in Session.Devices)
        {
            if (device.Id == bus.OutputDeviceId)
            {
                return device.Name;
            }
        }

        return bus.OutputDeviceId.Length == 0 ? L["bus.none"] : bus.OutputDeviceId;
    }

    IEnumerable<DeviceInfo> Renders =>
        Session.Devices.Where(device => device.Direction.Equals("Render", StringComparison.OrdinalIgnoreCase));

    async Task SetOutputAsync(BusState bus, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetBusOutputDevice = new SetBusOutputDevice
            {
                BusIndex = bus.Index,
                DeviceId = arguments.Value?.ToString() ?? string.Empty
            }
        });

    async Task SetColourAsync(BusState bus, string colour) =>
        await Session.ApplyAsync(new Command
        {
            SetBusColour = new SetBusColour { BusIndex = bus.Index, Colour = colour }
        });

    async Task SetRoleAsync(BusState bus, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetBusRole = new SetBusRole { BusIndex = bus.Index, Role = arguments.Value?.ToString() ?? bus.Role }
        });

    async Task RenameAsync(BusState bus, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetBusName = new SetBusName { BusIndex = bus.Index, Name = arguments.Value?.ToString() ?? bus.Name }
        });

    async Task RemoveAsync(BusState bus)
    {
        await Session.ApplyAsync(new Command { RemoveBus = new RemoveBus { BusIndex = bus.Index } });

        Shell.CloseOverlay();
    }
}
