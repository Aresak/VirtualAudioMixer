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

/// <summary>The code behind <c>SendButton.razor</c>.</summary>
public partial class SendButton
{
    /// <summary>The strip this send comes from.</summary>
    [Parameter]
    [EditorRequired]
    public required ChannelState Channel { get; set; }

    /// <summary>The bus it goes to.</summary>
    [Parameter]
    [EditorRequired]
    public required BusState Bus { get; set; }

    bool IsExcluded => Bus.ExcludedChannels.Contains(Channel.Index);

    bool IsOn => Send?.State == "On";

    SendState? Send => Session.Console?.Sends
        .FirstOrDefault(send => send.ChannelIndex == Channel.Index && send.BusIndex == Bus.Index);

    string Classes
    {
        get
        {
            if (IsExcluded)
            {
                return "locked";
            }

            // A monitor send is brass and an output send is green, because switching the wrong one
            // has completely different consequences and they should not look alike.
            return IsOn
                ? Bus.Role.Equals("Monitor", StringComparison.OrdinalIgnoreCase) ? "on mon" : "on"
                : string.Empty;
        }
    }

    string Suffix => IsExcluded ? "N−1" : IsOn ? "0.0" : "—";

    string Title => IsExcluded ? L["send.lockedWhy"] : Bus.Name;

    async Task ToggleAsync()
    {
        if (IsExcluded)
        {
            // Not disabled, and not silent either. An operator who clicks a locked send gets told why
            // it is locked; a button that does nothing teaches them the console is broken.
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
