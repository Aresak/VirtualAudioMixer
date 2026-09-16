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

/// <summary>The code behind <c>RecordingView.razor</c>.</summary>
public partial class RecordingView
{
    string directory = string.Empty;
    string refusal = string.Empty;

    RecordingState? Recording => Session.Console?.Recording;

    string Duration(RecordingState recording)
    {
        if (Session.SampleRate <= 0)
        {
            return "—";
        }

        TimeSpan elapsed = TimeSpan.FromSeconds(recording.FramesWritten / (double)Session.SampleRate);

        return elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
    }

    int Channels => Session.Console?.Channels.Count ?? 0;

    static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    /// <summary>Bytes a second, for every track being written.</summary>
    /// <remarks>
    /// Twenty-four bit at the session rate, one file per strip. The projection an operator is shown
    /// before they start has to be the real number, because the whole point of the guard is that it
    /// refuses before a meeting rather than during one.
    /// </remarks>
    long BytesPerSecond => (long)Math.Max(Session.SampleRate, 1) * 3 * Math.Max(Channels, 1);

    double HoursLeft(RecordingState recording) =>
        BytesPerSecond <= 0 ? 0 : recording.FreeBytes / (double)BytesPerSecond / 3600.0;

    // A full bar is a full disk, so it fills as the space goes. A bar that emptied would read as
    // "everything is fine" at exactly the moment it is not.
    static double DiskBar(RecordingState recording) =>
        recording.FreeBytes <= 0 ? 100 : Math.Clamp(100 - (recording.FreeBytes / (1024.0 * 1024 * 1024) / 10.0), 0, 100);

    async Task PickAsync()
    {
        if (await Platform.PickFolderAsync(L["recording.folder"]) is { } chosen)
        {
            directory = chosen;
        }
    }

    async Task StartAsync()
    {
        CommandReply reply = await Session.ApplyAsync(new Command
        {
            SetRecording = new SetRecording { Recording = true, Directory = directory }
        });

        // The disk's answer in the disk's own words. "There is room for forty minutes" is something
        // an operator can act on before a meeting; "recording failed" is not.
        refusal = reply.Accepted ? string.Empty : reply.Reason;
    }

    async Task StopAsync()
    {
        CommandReply reply = await Session.ApplyAsync(new Command
        {
            SetRecording = new SetRecording { Recording = false, Directory = string.Empty }
        });

        refusal = reply.Accepted ? string.Empty : reply.Reason;
    }
}
