using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Vam.Engine.Recording;

/// <summary>
/// What is already on disk under the recording root. E3.
/// </summary>
/// <remarks>
/// <para>
/// Every session lands in its own dated folder, and until now nothing in the console ever said those
/// folders existed. The question asked after a meeting — did last month's recording come out — was
/// answered by leaving the console and opening a file manager.
/// </para>
/// <para>
/// This reads the folder. It keeps no index, so a session deleted from disk disappears from the list
/// because it has gone, not because something remembered to remove it.
/// </para>
/// </remarks>
public sealed class RecordingCatalogue(ILogger<RecordingCatalogue> logger)
{
    /// <summary>The folder-name format a session is written under.</summary>
    public const string FolderFormat = "yyyy-MM-dd_HH-mm-ss";

    /// <summary>How many to report. The table is a list of recent sessions, not an archive.</summary>
    public const int Limit = 50;

    /// <summary>Reads the sessions under a root, newest first.</summary>
    /// <param name="root">Where recordings go.</param>
    /// <returns>What is there, or nothing when the root cannot be read.</returns>
    /// <remarks>
    /// A root that is missing, unreadable or on a network share that is not answering returns an
    /// empty list rather than throwing. The rest of the recording view has to keep working: an
    /// operator about to start a meeting does not care that last month's folder is on a disk nobody
    /// has plugged in.
    /// </remarks>
    public IReadOnlyList<RecordedSession> Read(string root)
    {
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }

        try
        {
            List<RecordedSession> sessions = [];

            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (Describe(directory) is { } session)
                {
                    sessions.Add(session);
                }
            }

            sessions.Sort(static (left, right) => right.StartedAt.CompareTo(left.StartedAt));

            return sessions.Count > Limit ? sessions[..Limit] : sessions;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "The recordings under {Root} could not be listed.", root);

            return [];
        }
    }

    RecordedSession? Describe(string directory)
    {
        try
        {
            string[] files = Directory.GetFiles(directory, "*.wav");

            if (files.Length == 0)
            {
                // A folder with no audio in it is not a session. A recording refused at the disk
                // guard leaves one behind, and listing it would be listing a meeting that was lost.
                return null;
            }

            long bytes = 0;

            foreach (string file in files)
            {
                bytes += new FileInfo(file).Length;
            }

            SessionManifest? manifest = SessionManifest.Read(directory);

            return new RecordedSession
            {
                Directory = directory,
                StartedAt = StartedFrom(directory, manifest),
                Duration = manifest is null ? null : TimeSpan.FromSeconds(manifest.DurationSeconds),
                Tracks = files.Length,
                Bytes = bytes,
                DroppedFrames = manifest?.DroppedFrames
            };
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "The session in {Directory} could not be read.", directory);

            return null;
        }
    }

    // The folder's own name first, because it is the session's start to the second and survives a
    // copy that rewrites the timestamps. Then the manifest, then the folder's creation time.
    static DateTimeOffset StartedFrom(string directory, SessionManifest? manifest)
    {
        string name = Path.GetFileName(directory);

        if (DateTime.TryParseExact(name, FolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return new DateTimeOffset(parsed, TimeZoneInfo.Local.GetUtcOffset(parsed));
        }

        return manifest?.StartedAt ?? new DateTimeOffset(Directory.GetCreationTime(directory));
    }
}
