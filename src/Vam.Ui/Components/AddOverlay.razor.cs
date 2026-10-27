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

/// <summary>The code behind <c>AddOverlay.razor</c>.</summary>
public partial class AddOverlay
{
    string captureId = string.Empty;
    string renderId = string.Empty;
    string channelName = string.Empty;
    string busName = string.Empty;
    string busRole = "Output";
    string refusal = string.Empty;
    bool participates = true;
    ElementReference busNameInput;
    bool isBusFocused;

    string Title => Shell.IsAddingBus ? L["mixer.addBusTitle"] : L["mixer.add"];

    IEnumerable<DeviceInfo> Captures =>
        Session.Devices.Where(device => device.Direction.Equals("Capture", StringComparison.OrdinalIgnoreCase));

    IEnumerable<DeviceInfo> Renders =>
        Session.Devices.Where(device => device.Direction.Equals("Render", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Focusing the bus name scrolls its section into view when the overlay came from the buses.
        // Tracked rather than tied to the first render, because the overlay may already be open.
        if (Shell.IsAddingBus == isBusFocused)
        {
            return;
        }

        isBusFocused = Shell.IsAddingBus;

        if (isBusFocused)
        {
            await busNameInput.FocusAsync();
        }
    }

    async Task AddChannelAsync()
    {
        string name = channelName.Length > 0
            ? channelName
            : Captures.FirstOrDefault(device => device.Id == captureId)?.Name ?? captureId;

        CommandReply reply = await Session.ApplyAsync(new Command
        {
            AddChannel = new AddChannel
            {
                Name = name,
                DeviceId = captureId,
                ChannelCount = 1,
                ParticipatesInAutomix = participates
            }
        });

        Settle(reply);
    }

    async Task AddBusAsync()
    {
        CommandReply reply = await Session.ApplyAsync(new Command
        {
            AddBus = new AddBus
            {
                Name = busName,
                Role = busRole,
                ChannelCount = 2,
                OutputDeviceId = renderId
            }
        });

        Settle(reply);
    }

    void Settle(CommandReply reply)
    {
        // A refusal is shown here rather than swallowed. The engine writes these for a person, and
        // the whole point of that is that the person gets to read them.
        refusal = reply.Accepted ? string.Empty : reply.Reason;

        if (reply.Accepted)
        {
            Shell.CloseOverlay();
        }
    }
}
