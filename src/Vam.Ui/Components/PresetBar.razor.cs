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

/// <summary>The code behind <c>PresetBar.razor</c>.</summary>
public partial class PresetBar
{
    string chosen = string.Empty;
    string saveAs = string.Empty;
    string refusal = string.Empty;

    /// <summary>Whose chain this is.</summary>
    [Parameter]
    [EditorRequired]
    public required ChainTarget Target { get; set; }

    /// <summary>The preset the live chain came from, or empty.</summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the live chain has drifted from it.</summary>
    [Parameter]
    public bool IsModified { get; set; }

    // Saving with the box empty saves over the preset this chain came from, which is what somebody
    // pressing save after an adjustment almost always means.
    string SaveName => saveAs.Length > 0 ? saveAs : Name;

    async Task ApplyAsync() => Settle(await Session.ApplyAsync(new Command
    {
        ApplyChainPreset = new ApplyChainPreset { Target = Target, Name = chosen }
    }));

    async Task SaveAsync()
    {
        Settle(await Session.ApplyAsync(new Command
        {
            SaveChainPreset = new SaveChainPreset { Target = Target, Name = SaveName }
        }));

        saveAs = string.Empty;

        await Session.RefreshPresetsAsync();
    }

    async Task DeleteAsync()
    {
        Settle(await Session.ApplyAsync(new Command
        {
            DeleteChainPreset = new DeleteChainPreset { Name = chosen }
        }));

        chosen = string.Empty;

        await Session.RefreshPresetsAsync();
    }

    void Settle(CommandReply reply) => refusal = reply.Accepted ? string.Empty : reply.Reason;
}
