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

/// <summary>The code behind <c>StatusBar.razor</c>.</summary>
public partial class StatusBar
{
    RecordingState? Recording => Session.Console?.Recording;

    EngineHealth? Health => Session.Console?.Health;

    bool IsAutomixOn => Session.Console?.Automix is { IsBypassed: false };

    string ConnectionDotClass => Session.Connection switch
    {
        ConnectionState.Connected => string.Empty,
        ConnectionState.Refused => "bad",
        _ => "rec"
    };

    string ConnectionText => Session.Connection switch
    {
        ConnectionState.Connected => L["status.connected"],
        ConnectionState.Connecting => L["status.connecting"],
        ConnectionState.Reconnecting => L["status.reconnecting"],
        ConnectionState.Refused => L["status.refused"],
        _ => L["status.offline"]
    };

    // Above seventy per cent the callback starts missing its deadline, so that is where the colour
    // changes rather than at some round number.
    string LoadColour => (Health?.Load ?? 0) switch
    {
        >= 1.0 => "var(--bad)",
        >= 0.7 => "var(--warn)",
        _ => "var(--ink)"
    };

    long Dropouts => Health?.Dropouts ?? 0;

    string Uptime => Health is { UptimeTicks: > 0 }
        ? TimeSpan.FromTicks(Health.UptimeTicks).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : "—";

    string RecordedText
    {
        get
        {
            if (Recording is null || Session.SampleRate <= 0)
            {
                return "—";
            }

            TimeSpan elapsed = TimeSpan.FromSeconds(Recording.FramesWritten / (double)Session.SampleRate);

            return elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    static string Percent(double fraction) =>
        (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0", CultureInfo.InvariantCulture) + " GB";

    async Task ToggleAutomixAsync()
    {
        AutomixState? automix = Session.Console?.Automix;

        if (automix is null)
        {
            return;
        }

        await Session.ApplyAsync(new Command
        {
            SetAutomix = new SetAutomix
            {
                Bypassed = !automix.IsBypassed,
                DepthDb = automix.DepthDb,
                ResponseMs = automix.ResponseMs
            }
        });
    }
}
