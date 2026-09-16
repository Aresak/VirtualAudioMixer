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

/// <summary>The code behind <c>ConfirmButton.razor</c>.</summary>
public partial class ConfirmButton
{
    bool isAsking;

    /// <summary>What the button says before it is pressed.</summary>
    [Parameter]
    [EditorRequired]
    public required string Label { get; set; }

    /// <summary>
    /// What is being asked.
    /// </summary>
    /// <remarks>
    /// A sentence naming what will happen, not "are you sure". An operator who has read "are you
    /// sure" fifty times has stopped reading it, which is how a confirmation stops being one.
    /// </remarks>
    [Parameter]
    [EditorRequired]
    public required string Question { get; set; }

    /// <summary>Raised when it was confirmed.</summary>
    [Parameter]
    public EventCallback Confirmed { get; set; }

    async Task YesAsync()
    {
        isAsking = false;

        await Confirmed.InvokeAsync();
    }
}
