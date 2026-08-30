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

/// <summary>The code behind <c>AutomixBand.razor</c>.</summary>
public partial class AutomixBand
{
    /// <summary>Columns kept. Thirty seconds at two a second.</summary>
    const int Capacity = 60;

    const int Width = 1100;

    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    readonly List<double[]> history = [];

    PeriodicTimer? timer;
    CancellationTokenSource? sampling;

    IReadOnlyList<ChannelState> Channels =>
        Session.Console?.Channels.Where(channel => channel.ParticipatesInAutomix).ToList() ?? [];

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        sampling = new CancellationTokenSource();
        timer = new PeriodicTimer(Interval);

        _ = SampleAsync(sampling.Token);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        sampling?.Cancel();
        sampling?.Dispose();
        timer?.Dispose();
    }

    async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (timer is not null && await timer.WaitForNextTickAsync(cancellationToken))
            {
                Sample();

                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // The view closed. The only way this loop ends.
        }
    }

    void Sample()
    {
        if (Session.Console?.Automix is not { } automix)
        {
            return;
        }

        double[] column = new double[automix.Shares.Count];

        for (int index = 0; index < column.Length; index++)
        {
            column[index] = automix.Shares[index];
        }

        history.Add(column);

        if (history.Count > Capacity)
        {
            history.RemoveRange(0, history.Count - Capacity);
        }
    }

    static string F(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
