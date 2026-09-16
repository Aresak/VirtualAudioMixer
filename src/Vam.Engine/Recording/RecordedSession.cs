namespace Vam.Engine.Recording;

/// <summary>
/// A session that has already been recorded, as the folder on disk describes it.
/// </summary>
/// <remarks>
/// Read from the folder rather than from an index. A catalogue kept beside the recordings is a
/// second thing that can disagree with them, and the folder is the record.
/// </remarks>
public sealed record RecordedSession
{
    /// <summary>The folder, as the engine sees it.</summary>
    public required string Directory { get; init; }

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>How long it ran, or null when nothing in the folder says.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>How many files it wrote.</summary>
    public int Tracks { get; init; }

    /// <summary>What it takes on disk.</summary>
    public long Bytes { get; init; }

    /// <summary>What it lost, or null when nothing in the folder says.</summary>
    public long? DroppedFrames { get; init; }
}
