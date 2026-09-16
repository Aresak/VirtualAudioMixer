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

/// <summary>The code behind <c>ChannelOverlay.razor</c>.</summary>
public partial class ChannelOverlay
{
    int Count => Session.Console?.Channels.Count ?? 0;

    IReadOnlyList<BusState> Buses => Session.Console?.Buses ?? [];

    IEnumerable<DeviceInfo> Captures =>
        Session.Devices.Where(device => device.Direction.Equals("Capture", StringComparison.OrdinalIgnoreCase));

    ChannelState? Channel =>
        Session.Console is { } console && Shell.SelectedChannel >= 0 && Shell.SelectedChannel < console.Channels.Count
            ? console.Channels[Shell.SelectedChannel]
            : null;

    static string Colour(ChannelState channel) =>
        string.IsNullOrWhiteSpace(channel.Colour) ? StripPalette.For(channel.Index) : channel.Colour;

    string Rates(ChannelState channel) =>
        Session.SampleRate.ToString(CultureInfo.InvariantCulture)
        + " / "
        + channel.MeasuredSampleRate.ToString("0.0", CultureInfo.InvariantCulture)
        + " Hz";

    string RemoveLabel(ChannelState channel) => L.Format("channel.remove", channel.Name);

    string RemovalQuestion(ChannelState channel) => L.Format("channel.removeWhy", channel.Name);

    static double LevelOf(ChannelState channel, BusState bus) =>
        bus.Index < channel.SendLevelsDb.Count ? channel.SendLevelsDb[bus.Index] : 0;

    async Task SetSendLevelAsync(ChannelState channel, BusState bus, ChangeEventArgs arguments)
    {
        if (!double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double decibels))
        {
            return;
        }

        await Session.ApplyAsync(new Command
        {
            SetSend = new SetSend
            {
                ChannelIndex = channel.Index,
                BusIndex = bus.Index,

                // The level is changed without changing whether the send is on. Turning a send on by
                // typing a number into it would surprise somebody who was adjusting a feed they had
                // deliberately muted.
                On = IsOn(channel, bus),
                Decibels = decibels
            }
        });
    }

    bool IsOn(ChannelState channel, BusState bus) =>
        Session.Console?.Sends
            .FirstOrDefault(send => send.ChannelIndex == channel.Index && send.BusIndex == bus.Index)?.State == "On";

    void Step(int delta)
    {
        int next = Shell.SelectedChannel + delta;

        if (next >= 0 && next < Count)
        {
            Shell.SelectedChannel = next;
        }
    }

    async Task RenameAsync(ChannelState channel, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetChannelName = new SetChannelName
            {
                ChannelIndex = channel.Index,
                Name = arguments.Value?.ToString() ?? channel.Name
            }
        });

    async Task SetDeviceAsync(ChannelState channel, ChangeEventArgs arguments) =>
        await Session.ApplyAsync(new Command
        {
            SetChannelDevice = new SetChannelDevice
            {
                ChannelIndex = channel.Index,
                DeviceId = arguments.Value?.ToString() ?? channel.DeviceId
            }
        });

    async Task SetColourAsync(ChannelState channel, string colour) =>
        await Session.ApplyAsync(new Command
        {
            SetChannelColour = new SetChannelColour { ChannelIndex = channel.Index, Colour = colour }
        });

    async Task SetTrimAsync(ChannelState channel, ChangeEventArgs arguments)
    {
        if (double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double decibels))
        {
            await Session.ApplyAsync(new Command
            {
                SetTrim = new SetTrim { ChannelIndex = channel.Index, Decibels = decibels }
            });
        }
    }

    async Task SetFaderAsync(ChannelState channel, ChangeEventArgs arguments)
    {
        if (double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double raw))
        {
            await Session.ApplyAsync(new Command
            {
                SetFader = new SetFader
                {
                    ChannelIndex = channel.Index,
                    Decibels = FaderScale.ToDecibels(raw / 1000.0)
                }
            });
        }
    }

    async Task SetWeightAsync(ChannelState channel, ChangeEventArgs arguments)
    {
        if (double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double weight))
        {
            await Session.ApplyAsync(new Command
            {
                SetAutomixWeight = new SetAutomixWeight { ChannelIndex = channel.Index, Weight = weight }
            });
        }
    }

    // Written the way a console prints it: a number of steps off centre, and the word for the middle
    // rather than a zero somebody has to interpret.
    string PanText(ChannelState channel) => Math.Abs(channel.Pan) < 0.005
        ? L["strip.centre"]
        : (channel.Pan < 0 ? "L" : "R") + (Math.Abs(channel.Pan) * 100).ToString("0", CultureInfo.InvariantCulture);

    async Task SetPanAsync(ChannelState channel, ChangeEventArgs arguments)
    {
        if (double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double pan))
        {
            await Session.ApplyAsync(new Command
            {
                SetPan = new SetPan { ChannelIndex = channel.Index, Pan = pan }
            });
        }
    }

    async Task SetFlagAsync(ChannelState channel, string flag, bool enabled) =>
        await Session.ApplyAsync(new Command
        {
            SetFlag = new SetFlag { ChannelIndex = channel.Index, Flag = flag, Enabled = enabled }
        });

    async Task RemoveAsync(ChannelState channel)
    {
        await Session.ApplyAsync(new Command
        {
            RemoveChannel = new RemoveChannel { ChannelIndex = channel.Index }
        });

        Shell.CloseOverlay();
    }
}
