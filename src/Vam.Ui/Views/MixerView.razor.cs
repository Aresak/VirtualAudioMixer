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

/// <summary>The code behind <c>MixerView.razor</c>.</summary>
public partial class MixerView
{
    /// <summary>Where the flag byte sits in a strip's ten. See MeterFrameCodec.</summary>
    const int FlagOffset = 8;

    readonly HashSet<int> latched = [];

    IJSObjectReference? meters;
    byte[] scratch = [];
    int shape = -1;
    int pending;
    int frameChannels;
    int frameBuses;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        Session.MeterFrame = OnMeterFrame;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        meters ??= await Js.InvokeAsync<IJSObjectReference>("import", "./_content/Vam.Ui/js/vam-meters.js");

        int current = Fingerprint();

        if (current == shape)
        {
            return;
        }

        // Only when the number of strips changed. Rebinding per frame would put a DOM query on the
        // meter path, which is precisely the cost this whole arrangement exists to avoid.
        shape = current;

        await meters.InvokeVoidAsync("bind");
    }

    /// <summary>Stops drawing and lets go of the module.</summary>
    public async ValueTask DisposeAsync()
    {
        Session.MeterFrame = null;

        Dispose();

        if (meters is not null)
        {
            try
            {
                await meters.InvokeVoidAsync("unbind");
                await meters.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser is already gone. Nothing to unbind, and nothing to report.
            }
        }
    }

    int Fingerprint()
    {
        ConsoleState? console = Session.Console;

        return console is null
            ? 0
            : HashCode.Combine(console.Channels.Count, console.Buses.Count, Shell.IsCompact, Shell.Overlay, Shell.IsMonitorBarOpen);
    }

    /// <summary>
    /// One meter frame, from the gRPC pump thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copied and handed to JavaScript, and never to Blazor. The copy is into a buffer sized when the
    /// console's shape changed, so a frame costs one memcpy of a couple of hundred bytes.
    /// </para>
    /// <para>
    /// <b>It drops frames rather than queueing them.</b> If the previous push has not finished — which
    /// on a slow link is the normal case — this one is thrown away. A meter that is a quarter of a
    /// second behind is worse than useless; a backlog of them is how a console stops answering a
    /// fader at all.
    /// </para>
    /// </remarks>
    void OnMeterFrame(ReadOnlySpan<byte> payload, int channelCount, int busCount)
    {
        if (meters is null || Interlocked.CompareExchange(ref pending, 1, 0) != 0)
        {
            return;
        }

        if (scratch.Length != payload.Length)
        {
            scratch = new byte[payload.Length];
        }

        payload.CopyTo(scratch);

        frameChannels = channelCount;
        frameBuses = busCount;

        DetectClipping(payload, channelCount);

        _ = PushAsync();
    }

    /// <summary>
    /// Reads the latched clip flags out of the frame on its way past. F1.
    /// </summary>
    /// <remarks>
    /// One byte per strip and a set comparison, and the shell only redraws when the set changed —
    /// which for a clip is a handful of times in a meeting. Everything else in the frame goes to the
    /// canvas and never touches the render tree.
    /// </remarks>
    /// <param name="payload">The packed frame.</param>
    /// <param name="channelCount">Strips in it.</param>
    void DetectClipping(ReadOnlySpan<byte> payload, int channelCount)
    {
        latched.Clear();

        for (int index = 0; index < channelCount; index++)
        {
            int at = (index * MeterFrameCodec.ChannelBytes) + FlagOffset;

            if (at < payload.Length && (payload[at] & (byte)MeterFlags.Clipped) != 0)
            {
                latched.Add(index);
            }
        }

        _ = InvokeAsync(() => Shell.SetClipped(latched));
    }

    async Task PushAsync()
    {
        try
        {
            if (meters is not null)
            {
                await meters.InvokeVoidAsync("frame", scratch, frameChannels, frameBuses);
            }
        }
        catch (JSException)
        {
            // A meter that failed to draw is not worth interrupting a meeting over. The next frame
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

    static bool IsMaster(BusState bus) =>
        bus.Role.Equals("Stream", StringComparison.OrdinalIgnoreCase);

    void ToggleDensity() => Shell.IsCompact = !Shell.IsCompact;

    async Task OnReorderedAsync((int From, int To) move) =>
        await Session.ApplyAsync(new Command
        {
            MoveChannel = new MoveChannel { FromIndex = move.From, ToIndex = move.To }
        });
}
