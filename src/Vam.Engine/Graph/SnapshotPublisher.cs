namespace Vam.Engine.Graph;

/// <summary>
/// Hands snapshots from the control thread to the audio thread, and takes the old ones back.
/// </summary>
/// <remarks>
/// <para>
/// Publication is one <see cref="Volatile.Write{T}"/> of a reference. The audio thread reads it once
/// per block and works from what it got, so a change either arrives before a block starts or after
/// it finishes — never during. That is the entire synchronisation story, and there is no lock in it.
/// </para>
/// <para>
/// <b>The retire queue is the part that is easy to leave out and expensive to omit.</b> Without it
/// the audio thread can be the last reference to an old snapshot, and dropping it there means the
/// collector frees a multi-megabyte pinned arena on the one thread that must not be doing that. So
/// the control thread keeps every retired snapshot until the audio thread has demonstrably moved
/// past it, and only then lets go.
/// </para>
/// </remarks>
public sealed class SnapshotPublisher
{
    readonly List<GraphSnapshot> retired = [];

    GraphSnapshot current;
    long lastSeenVersion = -1;

    /// <summary>Publishes the first snapshot.</summary>
    /// <param name="initial">What the audio thread reads until something replaces it.</param>
    public SnapshotPublisher(GraphSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);

        current = initial;
    }

    /// <summary>Snapshots retired but not yet released, because the audio thread may still hold one.</summary>
    public int PendingRetirements => retired.Count;

    /// <summary>The highest version the audio thread has acknowledged.</summary>
    public long LastSeenVersion => Volatile.Read(ref lastSeenVersion);

    /// <summary>
    /// The snapshot in force, without recording having seen it.
    /// </summary>
    /// <remarks>
    /// For the control thread, which needs the current parameters to build the next set from and
    /// must not pretend to be the audio thread while doing it — recording a version here would let
    /// the retire queue release a snapshot the audio thread is still rendering.
    /// </remarks>
    public GraphSnapshot Current => Volatile.Read(ref current);

    /// <summary>
    /// Takes the snapshot in force, and records having seen it.
    /// </summary>
    /// <remarks>
    /// Audio thread, once per block. Allocation-free: one acquiring read of a reference and one
    /// releasing write of a long.
    /// </remarks>
    /// <returns>The snapshot to render this block with.</returns>
    public GraphSnapshot Acquire()
    {
        GraphSnapshot snapshot = Volatile.Read(ref current);

        Volatile.Write(ref lastSeenVersion, snapshot.Version);

        return snapshot;
    }

    /// <summary>
    /// Puts a new snapshot in force. Control thread.
    /// </summary>
    /// <param name="snapshot">What the next block will render with.</param>
    public void Publish(GraphSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        GraphSnapshot previous = Volatile.Read(ref current);

        Volatile.Write(ref current, snapshot);

        // Held rather than dropped. The audio thread may be inside a block that took `previous`,
        // and letting it fall out of scope there would put a collection on the audio thread.
        if (!ReferenceEquals(previous, snapshot))
        {
            retired.Add(previous);
        }

        Collect();
    }

    /// <summary>
    /// Releases every retired snapshot the audio thread has provably moved past. Control thread.
    /// </summary>
    /// <remarks>
    /// Called after each publish, and worth calling on the control loop too: a session that stops
    /// changing anything would otherwise hold its last retired snapshot indefinitely.
    /// </remarks>
    public void Collect()
    {
        long seen = Volatile.Read(ref lastSeenVersion);

        // Strictly less than: a snapshot whose version equals what the audio thread last saw is the
        // one it may be rendering right now.
        retired.RemoveAll(snapshot => snapshot.Version < seen);
    }
}
