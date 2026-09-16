using System.Numerics;
namespace Vam.Engine.Diagnostics;

/// <summary>
/// Where the audio thread leaves a note about something going wrong. I2.
/// </summary>
/// <remarks>
/// <para>
/// A pre-allocated ring with an interlocked claim, so several audio threads can write into it
/// without any of them waiting for another. <c>Interlocked</c> is allowed below the line — it does
/// not block — where a lock is not.
/// </para>
/// <para>
/// <b>Full means the oldest is lost, and that is the right trade.</b> If the ring has filled up
/// between two drains then something is going very wrong indeed, and the first few records already
/// say what. Blocking to keep them all would be the audio thread waiting on a diagnostic, which is
/// exactly backwards.
/// </para>
/// </remarks>
public sealed class DropoutLog(int capacity = 1024)
{
    readonly DropoutRecord[] records =
        new DropoutRecord[BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 2))];

    long written;
    long drained;

    /// <summary>Records this log can hold before the oldest is lost.</summary>
    public int Capacity => records.Length;

    /// <summary>Everything ever recorded, including anything lost. Monotonic.</summary>
    public long TotalRecorded => Interlocked.Read(ref written);

    /// <summary>Records waiting to be drained.</summary>
    public int Pending => (int)Math.Min(Interlocked.Read(ref written) - Interlocked.Read(ref drained), records.Length);

    /// <summary>
    /// Notes one thing going wrong. Audio thread, inside the audio path.
    /// </summary>
    /// <remarks>
    /// One interlocked increment, one mask and one struct store. No allocation, no lock, no string.
    /// </remarks>
    /// <param name="record">What happened.</param>
    public void Record(DropoutRecord record)
    {
        long slot = Interlocked.Increment(ref written) - 1;

        records[(int)(slot & (records.Length - 1))] = record;
    }

    /// <summary>
    /// Notes one thing going wrong, filling in the timestamp.
    /// </summary>
    /// <param name="endpointIndex">Which device or bus.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="frames">How many frames it cost.</param>
    /// <param name="detail">One number whose meaning depends on the kind.</param>
    public void Record(int endpointIndex, DropoutKind kind, int frames, float detail = 0f) =>
        Record(new DropoutRecord(DateTimeOffset.UtcNow.UtcTicks, endpointIndex, kind, frames, detail));

    /// <summary>
    /// Takes everything waiting. Control thread.
    /// </summary>
    /// <param name="destination">Where to put them, oldest first.</param>
    /// <returns>How many were written. Fewer than <see cref="Pending"/> when the buffer is smaller.</returns>
    public int Drain(Span<DropoutRecord> destination)
    {
        long produced = Interlocked.Read(ref written);
        long consumed = Interlocked.Read(ref drained);

        // Anything older than the ring can hold has already been overwritten. Starting from the
        // oldest that still exists is more honest than reading whatever is in the slot.
        long from = Math.Max(consumed, produced - records.Length);
        int count = (int)Math.Min(produced - from, destination.Length);

        for (int index = 0; index < count; index++)
        {
            destination[index] = records[(int)((from + index) & (records.Length - 1))];
        }

        Interlocked.Exchange(ref drained, from + count);

        return count;
    }

    /// <summary>
    /// Copies what the ring holds without consuming it.
    /// </summary>
    /// <remarks>
    /// The diagnostics view reads; the pump drains. If the view drained, opening it would delete the
    /// lines that were about to be written to the log file, and the operator looking for a fault
    /// would be the reason nobody else could find it afterwards.
    /// </remarks>
    /// <param name="destination">Where they go, oldest first.</param>
    /// <returns>How many were written.</returns>
    public int Peek(Span<DropoutRecord> destination)
    {
        long end = Interlocked.Read(ref written);
        long start = Math.Max(end - Math.Min(records.Length, destination.Length), 0);
        int count = (int)(end - start);

        for (int index = 0; index < count; index++)
        {
            destination[index] = records[(int)((start + index) & (records.Length - 1))];
        }

        return count;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref written, 0);
        Interlocked.Exchange(ref drained, 0);

        Array.Clear(records);
    }
}
