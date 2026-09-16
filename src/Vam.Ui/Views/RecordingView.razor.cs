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

    int Buses => Session.Console?.Buses.Count ?? 0;

    // Tracks cannot be added to files that are already open, so a session keeps what it started
    // with. Saying so beats a switch that moves and changes nothing.
    string LockedBecause(RecordingState recording) =>
        recording.IsRecording ? L["recording.captureLocked"] : string.Empty;

    static string FormatLabel(string format) => format switch
    {
        "wav24" => "WAV · 24-bit",
        _ => format
    };

    string FormatTitle(RecordingState recording) => recording.IsRecording
        ? L["recording.captureLocked"]
        : L["recording.formatOnlyOne"];

    static string ExpectedHours(RecordingState recording) =>
        (recording.ExpectedSeconds / 3600.0).ToString("0.#", CultureInfo.InvariantCulture);

    static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

    /// <summary>
    /// How long the free space would last at the rate this session is projected to write.
    /// </summary>
    /// <remarks>
    /// Derived from the engine's own projection rather than recomputed here. The console used to
    /// work it out from the channel count, which ignored the bus tracks and was therefore optimistic
    /// about the one number the disk guard exists to get right.
    /// </remarks>
    static double? HoursLeft(RecordingState recording)
    {
        if (recording.ProjectedBytes <= 0 || recording.ExpectedSeconds <= 0)
        {
            // Nothing would be written, so there is no rate to divide by and no number of hours to
            // report. Null rather than zero: zero reads as a full disk.
            return null;
        }

        double bytesPerSecond = recording.ProjectedBytes / (double)recording.ExpectedSeconds;

        return recording.FreeBytes / bytesPerSecond / 3600.0;
    }

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

    async Task SetCaptureAsync(RecordingCapture captures)
    {
        CommandReply reply = await Session.ApplyAsync(new Command
        {
            SetCaptureOptions = new SetCaptureOptions { Captures = captures }
        });

        refusal = reply.Accepted ? string.Empty : reply.Reason;
    }

    Task OnFormatAsync(ChangeEventArgs arguments)
    {
        if (Recording is not { } recording || arguments.Value is not string format)
        {
            return Task.CompletedTask;
        }

        return SetCaptureAsync(new RecordingCapture(recording.Captures) { Format = format });
    }

    async Task SetAutoStartAsync(bool automatically)
    {
        CommandReply reply = await Session.ApplyAsync(new Command
        {
            SetStartupOptions = new SetStartupOptions
            {
                LoadLastConsole = Session.Console?.Startup?.LoadLastConsole ?? true,
                RecordAutomatically = automatically
            }
        });

        refusal = reply.Accepted ? string.Empty : reply.Reason;
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
