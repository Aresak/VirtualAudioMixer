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

/// <summary>The code behind <c>ChannelStrip.razor</c>.</summary>
public partial class ChannelStrip
{
    /// <summary>The strip to draw.</summary>
    [Parameter]
    [EditorRequired]
    public required ChannelState Channel { get; set; }

    /// <summary>The buses it can send to.</summary>
    [Parameter]
    public IReadOnlyList<BusState> Buses { get; set; } = [];

    /// <summary>Raised when a drag ends on this strip. U13.</summary>
    [Parameter]
    public EventCallback<(int From, int To)> Reordered { get; set; }

    string StateClass
    {
        get
        {
            List<string> classes = [];

            if (Shell.SelectedChannel == Channel.Index && Shell.Overlay == OverlayId.Channel)
            {
                classes.Add("sel");
            }

            // U16. A muted strip desaturates whole rather than growing a small badge, so it reads as
            // off from the other side of a room.
            if (Channel.IsMuted)
            {
                classes.Add("muted");
            }

            if (Channel.DeviceState is "Absent" or "Faulted")
            {
                classes.Add("absent");
            }

            return string.Join(' ', classes);
        }
    }

    // U5. Falls back to a stable colour derived from the index rather than to grey, so a console
    // nobody has coloured is still one where the strips are told apart at a glance.
    string Colour => string.IsNullOrWhiteSpace(Channel.Colour) ? StripPalette.For(Channel.Index) : Channel.Colour;

    string DeviceLine => Channel.DeviceState switch
    {
        "Absent" => L["strip.absent"],
        "Faulted" => L["strip.faulted"],
        _ => Channel.DeviceName
    };

    string NominalText => Session.SampleRate > 0
        ? (Session.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : "—";

    string MeasuredText => Channel.MeasuredSampleRate > 0
        ? (Channel.MeasuredSampleRate / 1000.0).ToString("0.000", CultureInfo.InvariantCulture)
        : "—";

    string DepthText => Session.Console?.Automix is { } automix
        ? automix.DepthDb.ToString("0", CultureInfo.InvariantCulture)
        : "-15";

    string GainText => Session.Console?.Automix is { } automix && Channel.Index < automix.GainsDb.Count
        ? automix.GainsDb[Channel.Index].ToString("0.0", CultureInfo.InvariantCulture)
        : "−∞";

    string ChainTitle =>
        $"{L["strip.chain"]}: {Channel.Chain.Count(link => !link.IsBypassed)}/{Channel.Chain.Count}";

    /// <summary>
    /// Where the gate opens, in decibels, or null when there is no gate in the chain.
    /// </summary>
    /// <remarks>
    /// Read off the chain rather than carried separately, because it is a modifier's parameter and
    /// duplicating it on the wire would give two places to disagree about one number.
    /// </remarks>
    double? GateThreshold
    {
        get
        {
            foreach (ModifierState link in Channel.Chain)
            {
                if (link.IsBypassed || !link.ModifierId.EndsWith("gate", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (ParameterState parameter in link.Parameters)
                {
                    if (parameter.Id.Contains("threshold", StringComparison.OrdinalIgnoreCase))
                    {
                        return parameter.Value;
                    }
                }
            }

            return null;
        }
    }

    // The meter's own scale, shared with vam-meters.js. A threshold drawn on a different scale from
    // the level beside it is worse than no threshold at all.
    static double Position(double decibels) =>
        Math.Clamp((Math.Clamp(decibels, -60, 6) + 60) / 66.0, 0, 1) * 100;

    int FaderPosition => (int)Math.Round(FaderScale.ToPosition(Channel.FaderDb) * 1000);

    // Read from the shell rather than from a meter frame: the frames never reach the render tree, so
    // the canvas tells the shell when a strip latches and the shell is what a component may read.
    bool HasClipped => Shell.IsClipped(Channel.Index);

    async Task ClearClipAsync()
    {
        if (!HasClipped)
        {
            return;
        }

        Shell.ClearClip(Channel.Index);

        await Session.ApplyAsync(new Command
        {
            ClearClip = new ClearClip { ChannelIndex = Channel.Index }
        });
    }

    void Open() => Shell.OpenChannel(Channel.Index);

    async Task OnFaderAsync(ChangeEventArgs arguments)
    {
        if (!double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double raw))
        {
            return;
        }

        double decibels = FaderScale.ToDecibels(raw / 1000.0);

        await Session.ApplyAsync(new Command
        {
            SetFader = new SetFader { ChannelIndex = Channel.Index, Decibels = decibels }
        });
    }

    Task ToggleMuteAsync() => SetFlagAsync("Muted", !Channel.IsMuted);

    Task ToggleSoloAsync() => SetFlagAsync("Soloed", !Channel.IsSoloed);

    Task ToggleMonoFoldAsync() => SetFlagAsync("MonoFold", !Channel.IsMonoFold);

    async Task SetFlagAsync(string flag, bool enabled) =>
        await Session.ApplyAsync(new Command
        {
            SetFlag = new SetFlag { ChannelIndex = Channel.Index, Flag = flag, Enabled = enabled }
        });

    void OnDragStart() => Shell.SelectedChannel = Channel.Index;

    async Task OnDropAsync()
    {
        if (Shell.IsReorderArmed && Shell.SelectedChannel >= 0 && Shell.SelectedChannel != Channel.Index)
        {
            await Reordered.InvokeAsync((Shell.SelectedChannel, Channel.Index));
        }
    }
}
