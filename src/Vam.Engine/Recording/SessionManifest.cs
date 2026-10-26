using System.Text.Json;

namespace Vam.Engine.Recording;

/// <summary>
/// The few facts about a finished session that its files cannot be asked for.
/// </summary>
/// <remarks>
/// <para>
/// Written into the session's own folder when it closes, not into an index somewhere else. A session
/// is then self-describing: copy the folder and it still knows what it is, delete it and nothing is
/// left pointing at something that has gone.
/// </para>
/// <para>
/// Dropped frames are the reason this exists. Size and track count are on disk to be counted, but a
/// recording that lost frames looks exactly like one that did not, and that is the one thing
/// somebody checking an old session actually needs to know.
/// </para>
/// </remarks>
public sealed record SessionManifest
{
    /// <summary>What it is called, inside the session folder.</summary>
    public const string FileName = "session.json";

    /// <summary>When the session started. A fallback: the folder's own name says it first.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>How long it ran.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>How many frames it lost, across every track.</summary>
    /// <remarks>
    /// The reason this file exists. Track count and size are on disk to be counted and are not
    /// repeated here: a fact written down twice is a fact that can disagree with itself.
    /// </remarks>
    public long DroppedFrames { get; init; }

    /// <summary>Reads one, or null when there is none or it cannot be read.</summary>
    /// <param name="directory">The session folder.</param>
    /// <returns>The manifest, or null.</returns>
    /// <remarks>
    /// Null rather than an exception for every failure. A folder somebody edited by hand is still a
    /// folder with a recording in it, and the table shows what it can.
    /// </remarks>
    public static SessionManifest? Read(string directory)
    {
        string path = Path.Combine(directory, FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(path));
        }
        catch (Exception failure) when (failure is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
