namespace Vam.Engine.Diagnostics;

/// <summary>
/// Drift and ring fill over time, per device. K2.
/// </summary>
/// <remarks>
/// <para>
/// The instantaneous numbers are already on the strip. This exists for the question the strip cannot
/// answer: whether the fill is holding steady or walking, which is the difference between a servo
/// doing its job and a device that will empty its ring somewhere in the second hour.
/// </para>
/// <para>
/// <b>Control thread only.</b> Written where the corrections are computed, which
/// <c>docs/audio-path.md</c> already places outside the line.
/// </para>
/// </remarks>
public sealed class DriftHistory(int capacity = 720)
{
    readonly DriftSample[] samples = new DriftSample[capacity];

    int next;
    int count;

    /// <summary>How many samples it keeps.</summary>
    public int Capacity => samples.Length;

    /// <summary>How many it holds.</summary>
    public int Count => count;

    /// <summary>Records one.</summary>
    /// <param name="sample">What was measured.</param>
    public void Record(DriftSample sample)
    {
        samples[next] = sample;
        next = (next + 1) % samples.Length;

        if (count < samples.Length)
        {
            count++;
        }
    }

    /// <summary>Copies the history out, oldest first.</summary>
    /// <param name="destination">Where it goes.</param>
    /// <returns>How many were written.</returns>
    public int CopyTo(Span<DriftSample> destination)
    {
        int written = Math.Min(count, destination.Length);
        int start = count < samples.Length ? 0 : next;

        for (int index = 0; index < written; index++)
        {
            // Walked from the oldest kept sample rather than from zero, so a full ring comes back in
            // the order it happened instead of wrapping in the middle of the chart.
            destination[index] = samples[(start + (count - written) + index) % samples.Length];
        }

        return written;
    }
}
