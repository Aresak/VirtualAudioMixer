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

/// <summary>The code behind <c>Switch.razor</c>.</summary>
public partial class Switch
{
    /// <summary>What it switches.</summary>
    [Parameter]
    [EditorRequired]
    public required string Label { get; set; }

    /// <summary>How to set it well. U14.</summary>
    [Parameter]
    public string Help { get; set; } = string.Empty;

    /// <summary>Whether it is on.</summary>
    [Parameter]
    public bool Checked { get; set; }

    /// <summary>Raised when somebody changed it.</summary>
    [Parameter]
    public EventCallback<bool> Changed { get; set; }

    Task OnChangedAsync(ChangeEventArgs arguments) => Changed.InvokeAsync(arguments.Value is true);
}
