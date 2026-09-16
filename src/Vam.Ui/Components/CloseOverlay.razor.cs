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

/// <summary>The code behind <c>CloseOverlay.razor</c>.</summary>
public partial class CloseOverlay
{
    /// <summary>The handshake with the window.</summary>
    [Inject]
    public required ShutdownPrompt Prompt { get; set; }

    /// <summary>What can stop the engine.</summary>
    [Inject]
    public required EngineConnector Connector { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        Prompt.Asked += OnAsked;
    }

    /// <remarks>
    /// Raised by the window's close handler, which is not on the renderer's thread. Discarded rather
    /// than awaited: the host has already cancelled its close and is waiting to be released, not
    /// waiting for a redraw.
    /// </remarks>
    void OnAsked() => _ = InvokeAsync(() =>
    {
        // Nothing to ask about when the engine is somebody else's: closing this window does not end
        // that meeting, and offering to would be offering to end it by accident.
        if (Prompt.IsAsking && !Connector.CanStopEngine)
        {
            Prompt.Release();

            return;
        }

        StateHasChanged();
    });

    async Task StopAndCloseAsync()
    {
        await Connector.StopEngineAsync("The console was closed.");

        Prompt.Release();
    }

    void LeaveAndClose() => Prompt.Release();

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => Prompt.Asked -= OnAsked;
}
