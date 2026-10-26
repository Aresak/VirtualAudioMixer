using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Components;
using Vam.Ui.Extensions;
using Vam.Ui.Localization;
using Vam.Ui.Services;
using Vam.Ui.State;
using Vam.Ui.Views;

namespace Vam.Ui.Views;

/// <summary>The code behind <c>SettingsView.razor</c>.</summary>
public partial class SettingsView
{
    string recordings = string.Empty;
    string pathProblem = string.Empty;

    string typed = string.Empty;

    /// <summary>Where the console is pointed, and what points it somewhere else.</summary>
    [Inject]
    public required EngineConnector Connector { get; set; }

    StartupOptions? Startup => Session.Console?.Startup;

    EnginePaths? Paths => Session.EnginePaths();

    static IEnumerable<MeterBallistics> Ballistics => Enum.GetValues<MeterBallistics>();

    // What the engine publishes at, and what a client that cannot keep up should draw. Nothing
    // between them, and nothing above: a console cannot draw frames it has not been sent.
    static IEnumerable<int> MeterRates =>
        [ShellState.DefaultMeterFramesPerSecond, ShellState.SlowMeterFramesPerSecond];

    /// <summary>This host, for the folder picker and the automatic start.</summary>
    [Inject]
    public required IPlatformServices Platform { get; set; }

    // What the box shows: whatever was typed, and the engine's own path until something is. A console
    // that connected after this view opened would otherwise show an empty box over a live engine.
    string Typed => recordings.Length > 0 ? recordings : Paths?.Recordings ?? string.Empty;

    void OnRecordingsTyped(ChangeEventArgs arguments) => recordings = arguments.Value as string ?? string.Empty;

    async Task BrowseAsync()
    {
        if (await Platform.PickFolderAsync(L["settings.recordings"]) is { } chosen)
        {
            recordings = chosen;

            await ApplyRecordingsAsync();
        }
    }

    Task ResetRecordingsAsync() => SendRecordingsAsync(new SetRecordingsPath { Reset = true });

    Task ApplyRecordingsAsync()
    {
        if (Typed.ToRecordingsPath() is not { } command)
        {
            pathProblem = L["settings.noFolder"];

            return Task.CompletedTask;
        }

        return SendRecordingsAsync(command);
    }

    async Task SendRecordingsAsync(SetRecordingsPath command)
    {
        CommandReply reply = await Session.ApplyAsync(new Command { SetRecordingsPath = command });

        // Typed goes back to showing the engine's answer, whatever the engine made of it.
        recordings = string.Empty;

        pathProblem = reply.Accepted ? string.Empty : reply.Reason;

        await Session.RefreshAsync();
    }

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
