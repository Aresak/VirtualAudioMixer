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
    /// <summary>How often the disk is measured.</summary>
    static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(30);

    readonly CancellationTokenSource stopping = new();
    readonly DiskGuard guard;

    Thread? reader;
    string folder;
    long freeBytes;

    /// <summary>Starts watching a folder, and takes the first reading now.</summary>
    /// <param name="guard">What measures the disk.</param>
    /// <param name="directory">The folder to measure. It does not have to exist yet.</param>
    public DiskSpaceWatch(DiskGuard guard, string directory)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.guard = guard;
        folder = directory;

        Read();
    }

    /// <summary>The folder being measured.</summary>
    public string Directory => Volatile.Read(ref folder);

    /// <summary>Bytes free where recordings go, or zero before anything could be read.</summary>
    public long FreeBytes => Interlocked.Read(ref freeBytes);

    /// <summary>Starts re-reading in the background.</summary>
    public void Start()
    {
        if (reader is not null)
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

        Volatile.Write(ref folder, directory);

        // Now rather than at the next pass: a recording that just started on another drive would
        // otherwise be shown beside the previous drive's figure for half a minute.
        Read();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        stopping.Cancel();

        reader?.Join(TimeSpan.FromSeconds(5));
        reader = null;

        stopping.Dispose();
    }

    void Run()
    {
        // Waits on the token rather than sleeping, so stopping the engine does not take half a
        // minute to return.
        while (!stopping.Token.WaitHandle.WaitOne(ReadInterval))
        {
            Read();
        }
    }

    void Read()
    {
        if (guard.FreeBytesAt(Directory) is { } free)
        {
            Interlocked.Exchange(ref freeBytes, free);
        }

        // A reading that failed leaves the last good one standing. A share that blinked is not a
        // disk that emptied, and zero is what the console draws as "no room at all".
    }
}
