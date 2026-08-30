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

/// <summary>The code behind <c>MatrixCell.razor</c>.</summary>
public partial class MatrixCell
{
    /// <summary>The strip this cell is on.</summary>
    [Parameter]
    [EditorRequired]
    public required ChannelState Channel { get; set; }

    /// <summary>The bus this cell is under.</summary>
    [Parameter]
    [EditorRequired]
    public required BusState Bus { get; set; }

    bool IsExcluded => Bus.ExcludedChannels.Contains(Channel.Index);

    bool IsOn => Session.Console?.Sends
        .FirstOrDefault(send => send.ChannelIndex == Channel.Index && send.BusIndex == Bus.Index)?.State == "On";

    async Task ToggleAsync()
    {
        if (IsExcluded)
        {
            return;
        }

        await Session.ApplyAsync(new Command
        {
            SetSend = new SetSend
            {
                ChannelIndex = Channel.Index,
                BusIndex = Bus.Index,
                On = !IsOn,
                Decibels = 0
            }
        });
    }
}
