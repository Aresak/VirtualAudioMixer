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

/// <summary>The code behind <c>InlineHelp.razor</c>.</summary>
public partial class InlineHelp
{
    bool isOpen;

    /// <summary>What is being explained.</summary>
    [Parameter]
    [EditorRequired]
    public required string Title { get; set; }

    /// <summary>How to set it well.</summary>
    [Parameter]
    public string Body { get; set; } = string.Empty;
}
