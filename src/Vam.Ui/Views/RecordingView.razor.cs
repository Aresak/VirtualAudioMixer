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

/// <summary>The code behind <c>RecordingView.razor</c>.</summary>
public partial class RecordingView
{
    string refusal = string.Empty;

    IReadOnlyList<PastSession>? sessions;
    bool sessionsTruncated;
    bool sessionsAsked;

    /// <summary>Whether the engine's recordings are on a disk this console can show.</summary>
    [Inject]
    public required EngineConnector Connector { get; set; }

    RecordingState? Recording => Session.Console?.Recording;

    // Null from an engine older than this console, which sends no capture options at all.
    RecordingCapture? Captures => Session.Supports(EngineFeatures.RecordingCaptures)
        ? Recording?.Captures
        : null;

    // Asked only once connected: before then there is no engine to be older than this console.
    bool HasPastSessions => Session.Connection != ConnectionState.Connected
        || Session.Supports(EngineFeatures.PastSessions);

    // Two separate questions, and both have to be yes: this host can show a folder at all, and the
    // engine whose folder it is runs on this machine.
    bool CanOpenFolders => Platform.CanOpenFolders && Connector.CanStopEngine;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadSessionsAsync();
    }

    async Task LoadSessionsAsync()
    {
        // Three outcomes, not two. The engine may be still coming up when this view is opened, and a
        // console that leaves "reading the folder" on screen for the life of the component is
        // claiming to still be working when it has given up.
        if (!HasPastSessions)
        {
            return;
        }

        PastSessionList? list = await Session.GetPastSessionsAsync();

        sessionsAsked = true;
        sessions = list?.Sessions;
        sessionsTruncated = list?.Truncated ?? false;

        await InvokeAsync(StateHasChanged);
    }

    static string Started(PastSession session) =>
        DateTimeOffset.FromUnixTimeSeconds(session.StartedUnixSeconds)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    // A dash rather than 0:00:00 when the folder does not say. A session recorded before the engine
    // wrote a manifest has files to count and nothing that remembers how long it ran.
    static string Ran(PastSession session) => session.DurationSeconds < 0
        ? "—"
        : Hours(TimeSpan.FromSeconds(session.DurationSeconds));

    // Total hours, so a session that ran past midnight does not wrap to a short one.
    static string Hours(TimeSpan ran) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)ran.TotalHours:00}:{ran.Minutes:00}:{ran.Seconds:00}");

    async Task OpenAsync(PastSession session)
    {
        string? problem = await Platform.OpenFolderAsync(session.Directory);

        // A key where the console owns the words, the operating system's own sentence where it does
        // not. A key the tables do not have renders as itself, so both arrive readable.
        refusal = problem is null ? string.Empty : L[problem];
    }

    // Two reasons the button is missing, and they are different things to tell somebody.
    string NoOpenBecause => Platform.CanOpenFolders ? "recording.openElsewhere" : "recording.openNoHost";
    EnginePaths? Paths => Session.Console?.Paths;

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
        if (Captures is not { } captures || arguments.Value is not string format)
        {
            return Task.CompletedTask;
        }

        return SetCaptureAsync(new RecordingCapture(captures) { Format = format });
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
            // No folder: the engine records into the root it has been configured with, and that
            // root has one home, on the settings view.
            SetRecording = new SetRecording { Recording = true }
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

        // The session that just closed is one of the past ones now, and an operator who stopped a
        // recording looks at that table next.
        await LoadSessionsAsync();
    }
}
