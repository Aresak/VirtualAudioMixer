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

/// <summary>The code behind <c>AutomixView.razor</c>.</summary>
public partial class AutomixView
{
    AutomixState? Automix => Session.Console?.Automix;

    IReadOnlyList<ChannelState> Channels => Session.Console?.Channels ?? [];

    static string Share(AutomixState automix, int index) =>
        index < automix.Shares.Count
            ? (automix.Shares[index] * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "—";

    static string Gain(AutomixState automix, int index) =>
        index < automix.GainsDb.Count
            ? automix.GainsDb[index].ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB"
            : "—";

    Task SetDepthAsync(ChangeEventArgs arguments) =>
        double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double depth)
            ? SendAsync(Automix?.IsBypassed ?? true, depth, Automix?.ResponseMs ?? 120)
            : Task.CompletedTask;

    Task SetResponseAsync(ChangeEventArgs arguments) =>
        double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double response)
            ? SendAsync(Automix?.IsBypassed ?? true, Automix?.DepthDb ?? -15, response)
            : Task.CompletedTask;

    async Task SendAsync(bool bypassed, double depth, double response) =>
        await Session.ApplyAsync(new Command
        {
            SetAutomix = new SetAutomix { Bypassed = bypassed, DepthDb = depth, ResponseMs = response }
        });
}
