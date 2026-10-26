namespace Vam.Engine.Recording;

/// <summary>
/// Keeps a current reading of how much room is left where recordings go. E5.
/// </summary>
/// <remarks>
/// <para>
/// Re-read while the session runs rather than measured once when it started. The figure is there to
/// warn an operator during a long meeting, and a number taken three hours ago says nothing about
/// the disk now.
/// </para>
/// <para>
/// On a thread of its own, not on the control loop. A recording folder is allowed to be a network
/// share, and a share that has gone away takes seconds to answer — the control loop drains commands,
/// polls devices and publishes meters, so measuring a disk there would freeze every console while
/// the audio carried on.
/// </para>
/// </remarks>
public sealed class DiskSpaceWatch : IDisposable
{
    /// <summary>How often the disk is measured when nothing says otherwise.</summary>
    public static readonly TimeSpan DefaultReadInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long disposal waits for a reading to come back.</summary>
    static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

    readonly CancellationTokenSource stopping = new();
    readonly DiskGuard guard;
    readonly TimeSpan interval;

    Thread? reader;
    string folder;
    long freeBytes;
    long measuredAtTicks;
    long generation;
    bool isDisposed;

    /// <summary>Starts watching a folder, and takes the first reading now.</summary>
    /// <param name="guard">What measures the disk.</param>
    /// <param name="directory">The folder to measure. It does not have to exist yet.</param>
    /// <param name="readInterval">How often to re-measure, or null for <see cref="DefaultReadInterval"/>.</param>
    public DiskSpaceWatch(DiskGuard guard, string directory, TimeSpan? readInterval = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        interval = readInterval ?? DefaultReadInterval;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        this.guard = guard;
        folder = directory;

        Read();
    }

    /// <summary>The folder being measured.</summary>
    public string Directory => Volatile.Read(ref folder);

    /// <summary>Bytes free where recordings go, or zero before anything could be read.</summary>
    public long FreeBytes => Interlocked.Read(ref freeBytes);

    /// <summary>When the figure was taken, or the epoch when nothing has been read.</summary>
    public DateTimeOffset MeasuredAt => new(Interlocked.Read(ref measuredAtTicks), TimeSpan.Zero);

    /// <summary>Starts re-reading in the background.</summary>
    public void Start()
    {
        if (reader is not null || isDisposed)
        {
            return;
        }

        reader = new Thread(Run)
        {
            IsBackground = true,
            Name = "disk-space-watch"
        };

        reader.Start();
    }

    /// <summary>
    /// Points the watch at another folder and measures it now.
    /// </summary>
    /// <param name="directory">The folder to measure from here on.</param>
    public void Watch(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // Before the folder changes, so a reading already under way on the old drive knows it has
        // been overtaken and drops its answer instead of overwriting this one.
        Interlocked.Increment(ref generation);

        Volatile.Write(ref folder, directory);

        // Now rather than at the next pass: a recording that just started on another drive would
        // otherwise be shown beside the previous drive's figure for half a minute.
        Read();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        stopping.Cancel();

        // The source is only disposed once the thread has actually left the loop. A reading still
        // blocked on a share that has gone away would come back to a disposed source, throw where
        // nothing catches it, and take the process down during shutdown.
        if (reader?.Join(JoinTimeout) is not false)
        {
            stopping.Dispose();
        }

        reader = null;
    }

    void Run()
    {
        WaitHandle stopped = stopping.Token.WaitHandle;

        while (!stopped.WaitOne(interval))
        {
            Read();
        }
    }

    void Read()
    {
        long taken = Interlocked.Read(ref generation);

        if (guard.FreeBytesAt(Directory) is not { } free)
        {
            // A reading that failed leaves the last good one standing. A share that blinked is not
            // a disk that emptied, and zero is what the console draws as "no room at all".
            return;
        }

        if (Interlocked.Read(ref generation) != taken)
        {
            return;
        }

        Interlocked.Exchange(ref freeBytes, free);
        Interlocked.Exchange(ref measuredAtTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
