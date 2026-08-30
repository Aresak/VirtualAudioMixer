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

/// <summary>The code behind <c>AppShell.razor</c>.</summary>
public partial class AppShell
{
    IJSObjectReference? faders;
    IJSObjectReference? drag;

    /// <summary>Finds the engine and connects to it.</summary>
    [Inject]
    public required EngineConnector Connector { get; set; }

    /// <summary>Where the pointer behaviour is attached.</summary>
    [Inject]
    public required IJSRuntime Js { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Started here rather than in each host's startup, so every host connects the same way and a
        // bug in one of them cannot be a bug in only one of them. The session's own lifetime belongs
        // to the container that built it, which is why nothing is disposed here.
        //
        // It finds the engine as well as connecting to it, and on a machine with none running it
        // starts one. Not awaited into a blank window: the console draws immediately and the status
        // bar says what is happening, which is the same thing it says when an engine drops mid-meeting.
        await Connector.StartAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Attached at the shell rather than per view. Sliders are in the mixer, the automix view, the
    /// chain editor and the strip overlay, and a fader that teleports in one place and not another is
    /// worse than one that teleports everywhere.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        faders = await Js.InvokeAsync<IJSObjectReference>("import", "./_content/Vam.Ui/js/vam-faders.js");

        await faders.InvokeVoidAsync("attach");

        drag = await Js.InvokeAsync<IJSObjectReference>("import", "./_content/Vam.Ui/js/vam-drag.js");

        await drag.InvokeVoidAsync("attach");
    }
}
