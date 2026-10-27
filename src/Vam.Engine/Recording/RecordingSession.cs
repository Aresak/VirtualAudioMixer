using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Vam.Engine.Recording;

/// <summary>
/// A recording in progress: its tracks, its writer thread and its watch on the disk. E3, E4 and E5.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is in the MVP even though nobody asked for it.</b> For a public body the multitrack is
/// the record of the meeting, and for this project it is what makes the automixer's first live
/// outing survivable — a session you cannot rebuild afterwards turns every processing bug into a
/// lost meeting.
/// </para>
/// <para>
/// The writer thread is the only thing that touches a file. The audio thread writes into rings and
/// never waits, so a disk that pauses costs a counted gap rather than a stalled broadcast.
/// </para>
/// </remarks>
public sealed class RecordingSession : IDisposable
{
    /// <summary>How often the writer thread wakes to move what is waiting.</summary>
    static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How often the disk is checked while recording.</summary>
    static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(30);

    readonly List<RecordingTrack> tracks = [];
    readonly CancellationTokenSource stopping = new();
    readonly DiskGuard guard;
    readonly ILogger<RecordingSession> logger;
    readonly string directory;

    Thread? writer;
    bool isStopped;

    /// <summary>Prepares a session in a folder.</summary>
    /// <param name="directory">Where the files go.</param>
    /// <param name="guard">What decides whether there is room.</param>
    /// <param name="logger">Where the loud things are said.</param>
    public RecordingSession(string directory, DiskGuard guard, ILogger<RecordingSession> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(logger);

        this.directory = directory;
        this.guard = guard;
        this.logger = logger;
    }

    /// <summary>The tracks being written.</summary>
    public IReadOnlyList<RecordingTrack> Tracks => tracks;

    /// <summary>The folder this session writes into.</summary>
    public string Directory => directory;

    /// <summary>What the session has put on disk so far, across every track.</summary>
    public long BytesWritten
    {
        get
        {
            long total = 0;

            foreach (RecordingTrack track in tracks)
            {
                total += track.BytesWritten;
            }

            return total;
        }
    }

    /// <summary>Whether the writer thread is running.</summary>
    public bool IsRecording => writer is not null;

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>
    /// Adds a track. Before <see cref="Start"/>, not during.
    /// </summary>
    /// <param name="name">What this track is.</param>
    /// <param name="format">Its rate, channels and block size.</param>
    /// <returns>The track.</returns>
    public RecordingTrack AddTrack(string name, RecordingFormat format, RecordingSource source)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (IsRecording)
        {
            throw new InvalidOperationException("Tracks cannot be added while recording.");
        }

        // Qualified, because this type has a Directory of its own and the folder is what is meant.
        System.IO.Directory.CreateDirectory(directory);

        RecordingTrack track = new(name, UniquePath(Sanitise(name), format.FileExtension), format, source);

        tracks.Add(track);

        return track;
    }

    /// <summary>
    /// Checks the disk and starts writing. E4 and E5.
    /// </summary>
    /// <param name="expectedDuration">How long the session is expected to run, for the projection.</param>
    /// <returns>What the disk guard decided. Recording only starts when it says so.</returns>
    public DiskVerdict Start(TimeSpan expectedDuration)
    {
        if (IsRecording)
        {
            return new DiskVerdict(true, 0, 0, "Already recording.");
        }

        DiskVerdict verdict = guard.CheckBeforeStart(directory, ProjectedBytes(expectedDuration));

        if (!verdict.CanStart)
        {
            return verdict;
        }

        StartedAt = DateTimeOffset.UtcNow;

        writer = new Thread(Run)
        {
            IsBackground = true,
            Name = "recording-writer"
        };

        writer.Start();

        logger.LogInformation(
            "Recording {TrackCount} tracks to {Directory}, projected {Projected} bytes.",
            tracks.Count,
            directory,
            verdict.ProjectedBytes);

        return verdict;
    }

    /// <summary>
    /// Drains everything left and closes every file with its sizes patched.
    /// </summary>
    /// <remarks>
    /// A file whose header was never patched is a file no player will open, and it will contain a
    /// whole meeting. This runs on every path out, including a fault.
    /// </remarks>
    public void Stop()
    {
        if (isStopped)
        {
            return;
        }

        isStopped = true;

        if (writer is not null)
        {
            stopping.Cancel();
            writer.Join(TimeSpan.FromSeconds(30));
            writer = null;
        }

        // Closed, not merely finished. After Stop the recording is over, and a file still held open
        // is a file nobody else can copy off the machine - which is the first thing somebody does
        // with the record of a meeting.
        foreach (RecordingTrack track in tracks)
        {
            track.Dispose();
        }

        Report();
        WriteManifest();
    }

    // Written when the session closes, into the session's own folder. Everything else in the table a
    // later console shows can be counted off the files; what a session lost cannot, and a recording
    // that dropped frames looks exactly like one that did not.
    void WriteManifest()
    {
        if (tracks.Count == 0)
        {
            return;
        }

        long dropped = 0;
        double seconds = 0;

        foreach (RecordingTrack track in tracks)
        {
            dropped += track.DroppedFrames;
            seconds = track.Format.SampleRate > 0 ? Math.Max(seconds, track.FramesWritten / (double)track.Format.SampleRate) : seconds;
        }

        SessionManifest manifest = new()
        {
            StartedAt = StartedAt,
            DurationSeconds = seconds,
            DroppedFrames = dropped
        };

        try
        {
            File.WriteAllText(
                Path.Combine(directory, SessionManifest.FileName),
                JsonSerializer.Serialize(manifest));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // The recording is already closed and on disk. A manifest that could not be written
            // costs a column in a table; failing the stop would cost the files.
            logger.LogWarning(failure, "The session manifest for {Directory} could not be written.", directory);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();

        tracks.Clear();
        stopping.Dispose();
    }

    /// <summary>The track recording a given channel or bus, if this session is recording it.</summary>
    /// <param name="source">Which channel or bus.</param>
    /// <returns>Its track, or null when this session does not record it.</returns>
    /// <remarks>
    /// The graph binds its taps through this rather than by position: which tracks a session opens
    /// is a choice now, and a tap bound by counting would write one source's audio into another
    /// source's file.
    /// </remarks>
    public RecordingTrack? Find(RecordingSource source)
    {
        foreach (RecordingTrack track in tracks)
        {
            if (track.Source == source)
            {
                return track;
            }
        }

        return null;
    }

    /// <summary>How large this session is expected to become.</summary>
    /// <param name="duration">How long it is expected to run.</param>
    /// <returns>Bytes.</returns>
    /// <remarks>
    /// Per track, at that track's own format. A stereo bus costs twice what a mono microphone does,
    /// and a projection that assumed one rate and one channel for everything was under by half on
    /// the one number the disk guard exists to get right.
    /// </remarks>
    public long ProjectedBytes(TimeSpan duration)
    {
        long total = 0;

        foreach (RecordingTrack track in tracks)
        {
            total += track.Format.BytesFor(duration);
        }

        return total;
    }

    static string Sanitise(string name)
    {
        Span<char> cleaned = stackalloc char[name.Length];

        for (int index = 0; index < name.Length; index++)
        {
            cleaned[index] = Array.IndexOf(Path.GetInvalidFileNameChars(), name[index]) >= 0 ? '_' : name[index];
        }

        return new string(cleaned);
    }

    /// <summary>A file no other track in this session writes to.</summary>
    /// <remarks>Two buses may share a name, and opening the same file twice fails the whole session.</remarks>
    string UniquePath(string stem, string extension)
    {
        string path = Path.Combine(directory, stem + extension);

        for (int suffix = 2; tracks.Exists(track => string.Equals(track.Path, path, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            path = Path.Combine(directory, $"{stem} {suffix}{extension}");
        }

        return path;
    }

    void Run()
    {
        TimeSpan sinceDiskCheck = TimeSpan.Zero;

        while (!stopping.IsCancellationRequested)
        {
            DrainAll();

            Thread.Sleep(DrainInterval);
            sinceDiskCheck += DrainInterval;

            if (sinceDiskCheck >= DiskCheckInterval)
            {
                sinceDiskCheck = TimeSpan.Zero;
                guard.IsRunningLow(directory);
            }
        }

        DrainAll();
    }

    void DrainAll()
    {
        foreach (RecordingTrack track in tracks)
        {
            try
            {
                track.Drain();
            }
            catch (Exception error)
            {
                // Loud, and only for this track. A failing USB drive taking one track down must not
                // take the others with it, and it must not be quiet about which one went.
                logger.LogError(error, "Writing {Track} failed. The other tracks continue.", track.Name);
            }
        }
    }

    void Report()
    {
        foreach (RecordingTrack track in tracks)
        {
            if (track.DroppedFrames > 0)
            {
                // A gap nobody was told about is a recording somebody will trust and should not.
                logger.LogWarning(
                    "{Track} lost {Frames} frames because the disk could not keep up.",
                    track.Name,
                    track.DroppedFrames);
            }
        }
    }
}
