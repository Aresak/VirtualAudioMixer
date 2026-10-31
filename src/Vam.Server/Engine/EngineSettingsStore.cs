using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Vam.Server.Engine;

/// <summary>
/// Keeps the choices an operator makes about recording and starting up across engine restarts. #136.
/// </summary>
/// <remarks>
/// <para>
/// The console says these apply "at the next start", and a next start is often a crash, a reboot or
/// a stop from the tray. An engine that forgot them would start recording again after an operator
/// said not to, into a folder they had moved away from, and nobody would be told.
/// </para>
/// <para>
/// Like the console, a file that cannot be read is not a reason to refuse to start. The defaults
/// apply and the log says why.
/// </para>
/// </remarks>
/// <param name="path">The file.</param>
/// <param name="logger">Where a file that cannot be read or written is reported.</param>
public sealed class EngineSettingsStore(string path, ILogger<EngineSettingsStore> logger)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Saves come from gRPC threads and the control thread at once. Unguarded, an older snapshot
    // could be moved into place after a newer one, and an operator's "no" would quietly become yes.
    readonly Lock gate = new();

    /// <summary>Writes the four choices, atomically.</summary>
    /// <param name="options">The engine's current options.</param>
    /// <remarks>
    /// A failure is logged rather than thrown. The choice still holds until the engine stops, and
    /// refusing it because the disk is full would be the worse of the two outcomes.
    /// </remarks>
    public void Save(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            lock (gate)
            {
                // Taken under the lock, so whichever save writes last writes the latest of everything.
                StoredSettings stored = new()
                {
                    RecordAutomatically = options.RecordAutomatically,
                    LoadLastConsole = options.LoadLastConsole,
                    Capture = options.Capture,
                    RecordingDirectory = options.RecordingDirectory
                };

                string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporary = path + ".writing";

                File.WriteAllText(temporary, JsonSerializer.Serialize(stored, Options));
                File.Move(temporary, path, overwrite: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                error,
                "The engine settings could not be saved to {Path}. They hold until the engine stops.",
                path);
        }
    }

    /// <summary>Applies what was saved onto the options, leaving anything unusable at its default.</summary>
    /// <param name="options">The options the engine was started with.</param>
    public void Restore(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(path))
        {
            return;
        }

        StoredSettings? stored;

        try
        {
            stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path), Options);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(error, "The engine settings at {Path} could not be read. Using the defaults.", path);
            return;
        }

        if (stored is null)
        {
            logger.LogWarning("The engine settings at {Path} were empty. Using the defaults.", path);
            return;
        }

        options.RecordAutomatically = stored.RecordAutomatically;
        options.LoadLastConsole = stored.LoadLastConsole;

        if (stored.Capture is { } capture)
        {
            if (!VamEngine.AvailableFormats.Contains(capture.Format))
            {
                logger.LogWarning(
                    "The saved recording format {Format} is not one this engine writes. Using the default capture.",
                    capture.Format);
            }
            else if (!capture.Inputs && !capture.StreamBus && !capture.AllBuses)
            {
                // The console refuses this selection, so only a damaged or hand-edited file has it.
                logger.LogWarning("The saved capture records nothing. Using the default capture.");
            }
            else
            {
                options.Capture = capture;
            }
        }

        if (!string.IsNullOrWhiteSpace(stored.RecordingDirectory))
        {
            if (Usable(stored.RecordingDirectory) is { } problem)
            {
                logger.LogWarning(
                    "The saved recording folder {Saved} {Problem}. Recordings go to {Default}.",
                    stored.RecordingDirectory,
                    problem,
                    options.RecordingDirectory);
            }
            else
            {
                options.RecordingDirectory = stored.RecordingDirectory;
            }
        }

        logger.LogInformation(
            "Restored the engine settings from {Path}: record automatically {Record}, load last console {Load}, recordings in {Recordings}.",
            path,
            options.RecordAutomatically,
            options.LoadLastConsole,
            options.RecordingDirectory);
    }

    /// <summary>Why a saved recording folder cannot be used, or null when it can.</summary>
    /// <remarks>
    /// A folder that does not exist yet is fine: it is created when the next session starts. A folder
    /// whose drive or share has gone is not, because the session would then fail to start at all and
    /// the meeting would go unrecorded rather than recorded into the default folder.
    /// </remarks>
    static string? Usable(string directory)
    {
        // The same rule SetRecordingDirectory applies.
        if (!Path.IsPathFullyQualified(directory))
        {
            return "is not a full path";
        }

        string? root = Path.GetPathRoot(directory);

        return string.IsNullOrEmpty(root) || Directory.Exists(root) ? null : "is on a drive or share that is not there";
    }

    /// <summary>The saved shape. Separate from the options so the file format can move independently.</summary>
    sealed record StoredSettings
    {
        public int Version { get; init; } = 1;

        public bool RecordAutomatically { get; init; } = true;

        public bool LoadLastConsole { get; init; } = true;

        public CaptureSelection? Capture { get; init; }

        public string? RecordingDirectory { get; init; }
    }
}
