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

/// <summary>The code behind <c>BusStrip.razor</c>.</summary>
public partial class BusStrip
{
    /// <summary>The bus to draw.</summary>
    [Parameter]
    [EditorRequired]
    public required BusState Bus { get; set; }

    /// <summary>Whether this is the bus the primary output takes. D3.</summary>
    [Parameter]
    public bool IsMaster { get; set; }

    // Anything at all counts as working. A limiter taking a tenth of a decibel off is a limiter that
    // caught something, and that is exactly what the light is for.
    bool IsLimiting => Bus.LimiterReductionDb < -0.05;

    string StateClass
    {
        get
        {
            List<string> classes = [];

            if (IsMaster)
            {
                classes.Add("master");
            }

            if (Bus.IsMuted)
            {
                classes.Add("muted");
            }

            if (Shell.Overlay == OverlayId.Bus && Shell.SelectedBus == Bus.Index)
            {
                classes.Add("sel");
            }

            return string.Join(' ', classes);
        }
    }

    // The roles are coloured apart because confusing them is the expensive mistake: a stream bus
    // goes out of the building and a monitor bus goes into somebody's ear.
    string NameColour
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Bus.Colour))
            {
                return Bus.Colour;
            }

            return Bus.Role.ToLowerInvariant() switch
            {
                "stream" => "var(--brass)",
                "monitor" => "var(--teal)",
                _ => "var(--ink)"
            };
        }
    }

    string OutputLine => Bus.OutputDeviceName.Length > 0
        ? Bus.OutputDeviceName
        : Bus.OutputDeviceId.Length > 0 ? Bus.OutputDeviceId : "— " + L["bus.none"];

    int Position => (int)Math.Round(FaderScale.ToPosition(Bus.GainDb) * 1000);

    void Open() => Shell.OpenBus(Bus.Index);

    async Task OnGainAsync(ChangeEventArgs arguments)
    {
        if (!double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double raw))
        {
            return;
        }

        await Session.ApplyAsync(new Command
        {
            SetBusGain = new SetBusGain { BusIndex = Bus.Index, Decibels = FaderScale.ToDecibels(raw / 1000.0) }
        });
    }

    async Task ToggleMuteAsync() =>
        await Session.ApplyAsync(new Command
        {
            SetBusMuted = new SetBusMuted { BusIndex = Bus.Index, Muted = !Bus.IsMuted }
        });
}
