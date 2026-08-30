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

/// <summary>The code behind <c>NavRail.razor</c>.</summary>
public partial class NavRail
{
    // Drawn as paths rather than pulled from an icon font: a console that runs in a WebView on a
    // machine with no network must not be waiting on a font to tell an operator where the mixer is.
    static readonly (ViewId View, string Key, string Path)[] Destinations =
    [
        (ViewId.Mixer, "view.mixer", "M5 3v18M12 3v18M19 3v18M2 8h6M9 14h6M16 6h6"),
        (ViewId.Automix, "view.automix", "M4 18V9M9 18V5M14 18v-6M19 18v-9M2 21h20"),
        (ViewId.Monitors, "view.monitors", "M11 5 6 9H2v6h4l5 4zM15.5 8.5a5 5 0 0 1 0 7M19 5a9 9 0 0 1 0 14"),
        (ViewId.Recording, "view.recording", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z"),
        (ViewId.Diagnostics, "view.diagnostics", "M3 12h4l3 8 4-16 3 8h4"),
        (ViewId.Settings, "view.settings", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-2.7 1.1V21a2 2 0 1 1-4 0v-.1A1.6 1.6 0 0 0 7.5 19.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1A1.6 1.6 0 0 0 3.6 14H3a2 2 0 1 1 0-4h.1A1.6 1.6 0 0 0 4.6 7.5l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H10a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 2.7 1.1l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V10a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1z")
    ];

    void Go(ViewId view)
    {
        // Leaving the mixer closes whatever was open over it. An overlay found still open on a
        // return three views later is a surprise, and this console does not surprise people.
        Shell.CloseOverlay();
        Shell.View = view;
    }
}
