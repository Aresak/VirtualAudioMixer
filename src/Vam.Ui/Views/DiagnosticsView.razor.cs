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

    DiagnosticsState? report;
    PeriodicTimer? timer;
    CancellationTokenSource? polling;
    DiagnosticsTab tab = DiagnosticsTab.Clocks;
    string filter = string.Empty;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        polling = new CancellationTokenSource();
        timer = new PeriodicTimer(PollInterval);

        _ = PollAsync(polling.Token);
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
                report = await Session.GetDiagnosticsAsync(cancellationToken);

                await InvokeAsync(StateHasChanged);
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

    IReadOnlyList<LogLine> Filtered(DiagnosticsState state) =>
        filter.Length == 0
            ? state.Log
            : [.. state.Log.Where(line => line.Message.Contains(filter, StringComparison.OrdinalIgnoreCase))];

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
