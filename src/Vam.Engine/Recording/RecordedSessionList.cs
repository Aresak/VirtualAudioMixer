namespace Vam.Engine.Recording;

/// <summary>
/// The recent sessions on disk, and whether there were more than the list holds.
/// </summary>
public sealed record RecordedSessionList
{
    /// <summary>Newest first, at most <see cref="RecordingCatalogue.Limit"/>.</summary>
    public required IReadOnlyList<RecordedSession> Sessions { get; init; }

    /// <summary>Whether older sessions were left out.</summary>
    public bool Truncated { get; init; }
}
