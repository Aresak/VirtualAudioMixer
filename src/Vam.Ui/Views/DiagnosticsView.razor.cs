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

/// <summary>The code behind <c>DiagnosticsView.razor</c>.</summary>
public partial class DiagnosticsView
{
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    static readonly (DiagnosticsTab Id, string Key)[] Tabs =
    [
        (DiagnosticsTab.Clocks, "diag.tabClocks"),
        (DiagnosticsTab.Performance, "diag.tabPerformance"),
        (DiagnosticsTab.Dropouts, "diag.tabDropouts"),
        (DiagnosticsTab.Log, "diag.tabLog")
    ];

    static readonly (LogLevelFilter Id, string Key)[] Levels =
    [
        (LogLevelFilter.All, "diag.levelAll"),
        (LogLevelFilter.Info, "diag.levelInfo"),
        (LogLevelFilter.Warning, "diag.levelWarning"),
        (LogLevelFilter.Error, "diag.levelError")
    ];

    const string LogModule = "./_content/Vam.Ui/js/vam-log.js";

    DiagnosticsState? report;
    PeriodicTimer? timer;
    CancellationTokenSource? polling;
    DiagnosticsTab tab = DiagnosticsTab.Clocks;
    string filter = string.Empty;
    LogLevelFilter level = LogLevelFilter.All;
    IJSObjectReference? logModule;
    ElementReference logBox;

    // The log as it was when the reader scrolled away from the newest line. Null while following.
    IReadOnlyList<LogLine>? held;
    int resumed;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        polling = new CancellationTokenSource();
        timer = new PeriodicTimer(PollInterval);

        _ = PollAsync(polling.Token);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            logModule = await Js.InvokeAsync<IJSObjectReference>("import", LogModule);
        }
    }

    /// <summary>Stops polling and lets go of the module.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (logModule is not null)
        {
            try
            {
                await logModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser is already gone.
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        polling?.Cancel();
        polling?.Dispose();
        timer?.Dispose();
    }

    async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            do
            {
                DiagnosticsState? next = await Session.GetDiagnosticsAsync(cancellationToken);

                await InvokeAsync(async () =>
                {
                    held = await IsReadingOlderAsync() ? held ?? report?.Log : null;
                    report = next;

                    StateHasChanged();
                });
            }
            while (timer is not null && await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The view was closed. That is the only way this loop ends.
        }
    }

    IReadOnlyList<string> ChannelNames =>
        Session.Console?.Channels.Select(channel => channel.Name).ToList() ?? [];

    string NameOf(int index) =>
        Session.Console is { } console && index < console.Channels.Count
            ? console.Channels[index].Name
            : $"#{index}";

    IReadOnlyList<LogLine> Shown(DiagnosticsState state) => (held ?? state.Log).Filter(level, filter);

    int Waiting(DiagnosticsState state) =>
        held is null ? 0 : state.Log.Filter(level, filter).CountNewerThan(held.Filter(level, filter));

    void Select(DiagnosticsTab id)
    {
        tab = id;
        held = null;
    }

    async Task<bool> IsReadingOlderAsync()
    {
        if (tab != DiagnosticsTab.Log || logModule is null)
        {
            return false;
        }

        int before = resumed;

        try
        {
            bool atNewest = await logModule.InvokeAsync<bool>("isAtNewest", logBox);

            // A click on "back to newest" during the call wins over what the call read.
            return !atNewest && before == resumed;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException
            or ObjectDisposedException or TaskCanceledException)
        {
            // Following the newest line is the safe answer, and the poll must not stop over it.
            return false;
        }
    }

    async Task ToNewestAsync()
    {
        held = null;
        resumed++;

        if (logModule is not null)
        {
            await logModule.InvokeVoidAsync("toNewest", logBox);
        }
    }

    static string When(long ticks) =>
        new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    static string Percent(double fraction) =>
        (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    // Sixty parts per million is where a resampler starts having to work noticeably. Coloured there
    // rather than at a round number, so the colour means something.
    static string DriftColour(double ppm) => Math.Abs(ppm) switch
    {
        >= 200 => "var(--bad)",
        >= 60 => "var(--warn)",
        _ => "var(--ink)"
    };

    static string LevelColour(string level) => level switch
    {
        "Error" or "Fatal" => "var(--bad)",
        "Warn" => "var(--warn)",
        _ => "var(--ink-mute)"
    };
}
