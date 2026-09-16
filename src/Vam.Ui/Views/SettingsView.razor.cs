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

namespace Vam.Ui.Views;

/// <summary>The code behind <c>SettingsView.razor</c>.</summary>
public partial class SettingsView
{
    string typed = string.Empty;

    /// <summary>Where the console is pointed, and what points it somewhere else.</summary>
    [Inject]
    public required EngineConnector Connector { get; set; }

    StartupOptions? Startup => Session.Console?.Startup;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        typed = Options.Address;

        Connector.Changed += OnConnectorChanged;
    }

    void OnConnectorChanged() => _ = InvokeAsync(StateHasChanged);

    void OnTyped(ChangeEventArgs args) => typed = args.Value?.ToString() ?? string.Empty;

    Task OnKeyAsync(KeyboardEventArgs args) => args.Key == "Enter" ? SwitchAsync() : Task.CompletedTask;

    async Task RestartAsync() => await Connector.RestartEngineAsync();

    async Task StopAsync() => await Connector.StopEngineAsync("Stopped from the console.");

    async Task SwitchAsync()
    {
        if (await Connector.SwitchToAsync(typed))
        {
            // Shown back completed, so somebody who typed a bare host can see the port that was
            // added rather than wondering whether it took.
            typed = Options.Address;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => Connector.Changed -= OnConnectorChanged;

    async Task SetStartupAsync(bool loadLast, bool autoRecord) =>
        await Session.ApplyAsync(new Command
        {
            SetStartupOptions = new SetStartupOptions
            {
                LoadLastConsole = loadLast,
                RecordAutomatically = autoRecord
            }
        });
}
