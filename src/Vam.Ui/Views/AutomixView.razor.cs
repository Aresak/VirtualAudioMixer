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

/// <summary>The code behind <c>AutomixView.razor</c>.</summary>
/// <remarks>
/// It owns the share surface the way the mixer owns the meter surface: it binds the band's canvas
/// and the channel rows after a render that changes their number, and forwards every meter frame
/// straight to JavaScript. C9's band is a thirty-second history of numbers that arrive twenty-five
/// times a second, and nothing on that path may touch the render tree.
/// </remarks>
public partial class AutomixView
{
    const string BandModule = "./_content/Vam.Ui/js/vam-band.js";

    IJSObjectReference? band;
    MeterFrameHandler? meterFrames;
    byte[] scratch = [];
    int? shape;
    int? look;
    int pending;
    int frameChannels;

    AutomixState? Automix => Session.Console?.Automix;

    IReadOnlyList<ChannelState> Channels => Session.Console?.Channels ?? [];

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        // Held in a field so that letting go of it later can tell whether the slot is still ours.
        // Views are swapped by rendering the new one and disposing the old one afterwards, so the
        // mixer's teardown runs after this line and must not clear a handler it no longer owns.
        meterFrames = OnMeterFrame;
        Session.TakeMeterFrames(meterFrames);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        band ??= await Js.InvokeAsync<IJSObjectReference>("import", BandModule);

        int currentShape = Fingerprint();
        int currentLook = Look();

        if (currentShape != shape)
        {
            shape = currentShape;
            look = currentLook;

            await band.InvokeVoidAsync("bind", Colours(), L["automix.bandStart"], L["automix.bandEnd"]);
        }
        else if (currentLook != look)
        {
            look = currentLook;

            await band.InvokeVoidAsync("restyle", Colours(), L["automix.bandStart"], L["automix.bandEnd"]);
        }
    }

    /// <summary>Stops drawing and lets go of the module.</summary>
    public async ValueTask DisposeAsync()
    {
        Session.ReleaseMeterFrames(meterFrames);

        Dispose();

        if (band is not null)
        {
            try
            {
                await band.InvokeVoidAsync("unbind");
                await band.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser is already gone. Nothing to unbind, and nothing to report.
            }
        }
    }

    /// <summary>
    /// The colour of every strip, in strip order, for the band and its legend.
    /// </summary>
    /// <remarks>
    /// Sent on binding rather than read per frame, and again when somebody picks one.
    /// </remarks>
    string[] Colours()
    {
        string[] colours = new string[Channels.Count];

        for (int index = 0; index < colours.Length; index++)
        {
            colours[index] = Channels[index].ToStripColour();
        }

        return colours;
    }

    int Fingerprint()
    {
        HashCode shapeOf = new();

        shapeOf.Add(Channels.Count);
        shapeOf.Add(Automix is null);

        foreach (ChannelState channel in Channels)
        {
            // The index too, because the rows are keyed on it: two strips swapping places replaces
            // the row elements, and a binding that did not notice would keep writing into the two
            // that had just been thrown away.
            shapeOf.Add(channel.Index);
        }

        return shapeOf.ToHashCode();
    }

    /// <summary>What only needs restyling: the colours and the language of the axis labels.</summary>
    int Look()
    {
        HashCode lookOf = new();

        lookOf.Add(L.Current.Code);

        foreach (ChannelState channel in Channels)
        {
            lookOf.Add(channel.Colour);
        }

        return lookOf.ToHashCode();
    }

    /// <summary>
    /// One meter frame, from the gRPC pump thread.
    /// </summary>
    /// <remarks>
    /// Copied and handed to JavaScript, never to Blazor, and dropped rather than queued when the
    /// previous push has not finished. The band is a picture of the last thirty seconds; a backlog
    /// of frames would draw a picture of thirty seconds that have already gone.
    /// </remarks>
    /// <param name="payload">The packed frame. Only valid for the duration of the call.</param>
    /// <param name="channelCount">Strips in this frame.</param>
    /// <param name="busCount">Buses in this frame.</param>
    void OnMeterFrame(ReadOnlySpan<byte> payload, int channelCount, int busCount)
    {
        if (band is null || Interlocked.CompareExchange(ref pending, 1, 0) != 0)
        {
            return;
        }

        if (scratch.Length != payload.Length)
        {
            scratch = new byte[payload.Length];
        }

        payload.CopyTo(scratch);

        frameChannels = channelCount;

        _ = PushAsync();
    }

    async Task PushAsync()
    {
        try
        {
            if (band is not null)
            {
                await band.InvokeVoidAsync("frame", scratch, frameChannels);
            }
        }
        catch (JSException)
        {
            // A band that failed to draw is not worth interrupting a meeting over. The next frame
            // is forty milliseconds away.
        }
        catch (JSDisconnectedException)
        {
            // The browser went away mid-frame.
        }
        catch (ObjectDisposedException)
        {
            // The view was closed between the copy and the call.
        }
        finally
        {
            Volatile.Write(ref pending, 0);
        }
    }

    Task SetDepthAsync(ChangeEventArgs arguments) =>
        double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double depth)
            ? SendAsync(Automix?.IsBypassed ?? true, depth, Automix?.ResponseMs ?? 120)
            : Task.CompletedTask;

    Task SetResponseAsync(ChangeEventArgs arguments) =>
        double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double response)
            ? SendAsync(Automix?.IsBypassed ?? true, Automix?.DepthDb ?? -15, response)
            : Task.CompletedTask;

    async Task SendAsync(bool bypassed, double depth, double response) =>
        await Session.ApplyAsync(new Command
        {
            SetAutomix = new SetAutomix { Bypassed = bypassed, DepthDb = depth, ResponseMs = response }
        });
}
