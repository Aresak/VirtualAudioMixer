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

/// <summary>The code behind <c>MonitorBar.razor</c>.</summary>
public partial class MonitorBar
{
    IReadOnlyList<BusState> Monitors =>
        Session.Console?.Buses
            .Where(bus => bus.Role.Equals("Monitor", StringComparison.OrdinalIgnoreCase))
            .ToList()
        ?? [];

    IReadOnlyList<ChannelState> Channels => Session.Console?.Channels ?? [];

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

        // The same command a send button sends, because a monitor source is a send. Nothing about
        // this row is a second mechanism — it is a different view of the matrix, and if it were its
        // own mechanism the two would disagree the first time somebody used both.
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
}
