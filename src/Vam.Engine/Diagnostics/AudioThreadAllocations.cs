namespace Vam.Engine.Diagnostics;

/// <summary>
/// What the audio thread allocated. K5, and the number that has to be zero.
/// </summary>
/// <remarks>
/// <para>
/// The diagnostics view's first row reads this, and it reads zero. Rule 1 says nothing in the audio
/// path allocates; a test asserts it on a quiet machine, and this asserts it on a real one, for
/// three hours, with a modifier somebody wrote last week in the chain. If it is ever not zero, that
/// is the bug, and it is worth more than the rest of the view put together.
/// </para>
/// <para>
/// <b>The measurement itself allocates nothing.</b> <c>GC.GetAllocatedBytesForCurrentThread</c> reads
/// a per-thread counter the runtime already maintains.
/// </para>
/// </remarks>
public sealed class AudioThreadAllocations
{
    long baseline;
    long total;
    long blocks;

    /// <summary>Total bytes the audio thread has allocated since it started.</summary>
    public long TotalBytes => total;

    /// <summary>How many blocks that is spread over.</summary>
    public long Blocks => blocks;

    /// <summary>Called at the top of a render callback.</summary>
    /// <remarks>Inside the audio path.</remarks>
    public void Begin() => baseline = GC.GetAllocatedBytesForCurrentThread();

    /// <summary>Called at the bottom of a render callback.</summary>
    /// <remarks>Inside the audio path.</remarks>
    public void End()
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread() - baseline;

        // Guarded rather than trusted: the counter is per thread, and a render that ran on a
        // different thread from the one that called Begin would otherwise report nonsense.
        if (allocated > 0)
        {
            total += allocated;
        }

        blocks++;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        total = 0;
        blocks = 0;
    }
}
