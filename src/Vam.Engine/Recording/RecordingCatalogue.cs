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

    /// <summary>Reads the sessions under one or more roots, newest first.</summary>
    /// <param name="roots">Where recordings go. The same folder named twice is read once.</param>
    /// <param name="excluding">The folder being recorded into right now, which is not a past session.</param>
    /// <returns>What is there, or nothing for a root that cannot be read.</returns>
    /// <remarks>
    /// A root that is missing, unreadable or on a network share that is not answering adds nothing
    /// rather than throwing. The rest of the recording view has to keep working: an operator about
    /// to start a meeting does not care that last month's folder is on a disk nobody has plugged in.
    /// </remarks>
    public RecordedSessionList Read(IEnumerable<string> roots, string excluding = "")
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        if (excluding.Length > 0)
        {
            seen.Add(Normalise(excluding));
        }

        List<RecordedSession> sessions = [];

        foreach (string root in roots.Where(root => root.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ReadRoot(root, seen, sessions);
        }

        sessions.Sort(static (left, right) => right.StartedAt.CompareTo(left.StartedAt));

        return sessions.Count > Limit
            ? new RecordedSessionList { Sessions = sessions[..Limit], Truncated = true }
            : new RecordedSessionList { Sessions = sessions };
    }

    /// <summary>The session in one folder, if it is one this catalogue would list.</summary>
    /// <param name="roots">Where recordings go.</param>
    /// <param name="directory">The folder, as <see cref="Read"/> gave it.</param>
    /// <returns>
    /// The session, or null when the folder is not a session with a manifest directly under one of the roots.
    /// </returns>
    /// <remarks>
    /// What stands between a console and deleting an arbitrary folder on the engine's machine. A
    /// session from before manifests, or one whose engine died before writing it, is listed but not
    /// found here.
    /// </remarks>
    public RecordedSession? Find(IEnumerable<string> roots, string directory)
    {
        if (directory.Length == 0 || !Path.IsPathFullyQualified(directory))
        {
            return null;
        }

        string folder = Normalise(directory);

        if (Path.GetDirectoryName(folder) is not { } parent ||
            !roots.Any(root => root.Length > 0 && string.Equals(Normalise(root), parent, StringComparison.OrdinalIgnoreCase)) ||
            !Directory.Exists(folder))
        {
            return null;
        }

        // Only a folder VAM closed. A root can be any folder a console names, and audio files under
        // it are not enough to make somebody's music collection a session.
        return SessionManifest.Read(folder) is null ? null : Describe(folder);
    }

    /// <summary>Whether two paths name the same folder.</summary>
    /// <param name="left">One folder.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they are the same folder.</returns>
    public static bool SameFolder(string left, string right) =>
        left.Length > 0 && right.Length > 0 &&
        string.Equals(Normalise(left), Normalise(right), StringComparison.OrdinalIgnoreCase);

    static string Normalise(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    void ReadRoot(string root, HashSet<string> seen, List<RecordedSession> sessions)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (seen.Add(Normalise(directory)) && Describe(directory) is { } session)
                {
                    sessions.Add(session);
                }
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "The recordings under {Root} could not be listed.", root);
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
