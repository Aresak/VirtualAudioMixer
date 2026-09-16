namespace Vam.Engine.Diagnostics;

/// <summary>
/// How long the render callback took, as a shape rather than a number. K4.
/// </summary>
/// <remarks>
/// <para>
/// A mean is the wrong statistic for this. A callback that is usually comfortable and occasionally
/// takes twice its budget is a completely different problem from one that sits near the edge all
/// afternoon, and both average to something that looks fine. The histogram shows which one is
/// happening.
/// </para>
/// <para>
/// <b>Inside the audio path.</b> Everything is allocated in the constructor; recording a sample is
/// one divide, one array increment and two compares.
/// </para>
/// </remarks>
public sealed class CallbackHistogram
{
    readonly long[] buckets;
    readonly double bucketWidth;

    long overruns;
    double worst;
    double recentWorst;

    /// <summary>Creates a histogram over a block budget.</summary>
    /// <param name="bucketCount">How many buckets. The last one collects everything past the end.</param>
    /// <param name="bucketWidthFraction">
    /// How wide each bucket is, as a fraction of a block. The default puts the block boundary at the
    /// twentieth bucket, so the interesting region is the part of the chart with resolution in it.
    /// </param>
    public CallbackHistogram(int bucketCount = 32, double bucketWidthFraction = 0.05)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketWidthFraction);

        buckets = new long[bucketCount];
        bucketWidth = bucketWidthFraction;
    }

    /// <summary>How wide each bucket is, as a fraction of a block.</summary>
    public double BucketWidthFraction => bucketWidth;

    /// <summary>How many buckets there are.</summary>
    public int BucketCount => buckets.Length;

    /// <summary>The worst callback since the last clear, as a fraction of a block.</summary>
    public double WorstFraction => worst;

    /// <summary>How many callbacks took longer than a block.</summary>
    /// <remarks>
    /// Not the same as a dropout. One long callback inside the device's buffer margin is absorbed;
    /// a run of them is what an operator hears.
    /// </remarks>
    public long Overruns => overruns;

    /// <summary>Records one callback.</summary>
    /// <remarks>Inside the audio path.</remarks>
    /// <param name="elapsedTicks">How long it took.</param>
    /// <param name="blockTicks">How long a block is worth.</param>
    public void Record(long elapsedTicks, long blockTicks)
    {
        if (blockTicks <= 0)
        {
            return;
        }

        double fraction = elapsedTicks / (double)blockTicks;
        int bucket = (int)(fraction / bucketWidth);

        if (bucket >= buckets.Length)
        {
            bucket = buckets.Length - 1;
        }
        else if (bucket < 0)
        {
            bucket = 0;
        }

        buckets[bucket]++;

        if (fraction > worst)
        {
            worst = fraction;
        }

        if (fraction > recentWorst)
        {
            recentWorst = fraction;
        }

        if (fraction >= 1.0)
        {
            overruns++;
        }
    }

    /// <summary>
    /// The worst callback since this was last asked, and forgets it.
    /// </summary>
    /// <remarks>
    /// The status bar wants "how close are we right now", which the all-time worst cannot answer: one
    /// bad block during startup would leave it reading ninety per cent for the rest of the evening
    /// and an operator would learn to ignore it. Read once a second by the control loop.
    /// </remarks>
    /// <returns>The worst fraction of a block seen since the last call.</returns>
    public double TakeRecentWorst()
    {
        double taken = recentWorst;

        recentWorst = 0;

        return taken;
    }

    /// <summary>Copies the buckets out, for a caller that owns the destination.</summary>
    /// <param name="destination">Where they go. Longer than <see cref="BucketCount"/> or it throws.</param>
    public void CopyTo(Span<long> destination) => buckets.AsSpan().CopyTo(destination);

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        Array.Clear(buckets);
        worst = 0;
        recentWorst = 0;
        overruns = 0;
    }
}
