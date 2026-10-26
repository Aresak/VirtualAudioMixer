using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Components;
using Vam.Ui.Extensions;
using Vam.Ui.Localization;
using Vam.Ui.Services;
using Vam.Ui.State;
using Vam.Ui.Views;

namespace Vam.Ui.Components;

/// <summary>The code behind <c>AutomixBand.razor</c>.</summary>
public partial class AutomixBand
{
    IReadOnlyList<ChannelState> Channels =>
        Session.Console?.Channels.Where(channel => channel.ParticipatesInAutomix).ToList() ?? [];
}
