namespace Vam.Engine.Devices.Clock;

/// <summary>
/// Works out how far a device's real clock sits from its nominal rate, by watching its ring
/// buffer fill.
/// </summary>
/// <remarks>
/// <para>
/// One instance per device. It reads <see cref="AudioRingBuffer.FillFrames"/> on a timer and
/// nothing else, and it runs entirely off the audio thread.
/// </para>
/// <para>
/// The consumer of a ring is the master clock, so by definition it consumes at exactly the nominal
/// rate. Whatever the fill does over time is therefore the producer's error: fill rising by one
/// frame a second means the device is running one frame a second fast.
/// </para>
/// <para>
/// <b>Slow on purpose.</b> The slope is fitted by least squares over a minute rather than
/// differenced between two readings, because a difference amplifies jitter and an estimator that
/// chases jitter is worse than doing nothing at all. Correcting drift is not urgent; correcting it
/// wrongly is harmful.
/// </para>
/// </remarks>
public sealed class DriftEstimator
{
    /// <summary>
    /// Beyond this, whatever is happening is not drift. Real devices sit within a few tens of ppm;
    /// hundreds means a wrong sample rate, a stalled thread, or a device lying about itself.
    /// </summary>
    public const double MaxPlausiblePpm = 500.0;

    /// <summary>
    /// How much more history is kept than the declared observation rate needs. Observing somewhat
    /// faster than declared then still leaves a full window rather than silently truncating it.
    /// </summary>
    const double CapacityHeadroom = 2.0;

    readonly double[] sampleTimes;
    readonly double[] sampleFills;
    readonly int capacity;
    readonly double windowSeconds;
    readonly int nominalRateHz;
    readonly int targetFillFrames;

    int count;
    int next;
    double elapsedSeconds;
    double lastIntervalSeconds;
    double slopeFramesPerSecond;

    /// <summary>Creates an estimator for one device.</summary>
    /// <param name="nominalRateHz">The rate the device claims.</param>
    /// <param name="targetFillFrames">
    /// Where the ring is meant to sit. Used only to notice the fill walking away from its band -
    /// holding it there is the servo's job, not this one's.
    /// </param>
    /// <param name="window">
    /// How much history the slope is fitted over. Tens of seconds; shorter starts tracking jitter.
    /// </param>
    /// <param name="observationInterval">
    /// How often <see cref="Observe"/> is expected to be called. Only used to size the history, but
    /// it has to be roughly right: observing far faster than declared fills the buffer before the
    /// window is covered, and the estimate then never settles.
    /// </param>
    public DriftEstimator(
        int nominalRateHz,
        int targetFillFrames,
        TimeSpan window,
        TimeSpan? observationInterval = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nominalRateHz, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(targetFillFrames);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        TimeSpan interval = observationInterval ?? TimeSpan.FromMilliseconds(250);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        this.nominalRateHz = nominalRateHz;
        this.targetFillFrames = targetFillFrames;
        windowSeconds = window.TotalSeconds;

        capacity = (int)Math.Ceiling(window.TotalSeconds / interval.TotalSeconds * CapacityHeadroom) + 8;
        sampleTimes = new double[capacity];
        sampleFills = new double[capacity];
    }

    /// <summary>The device's measured rate.</summary>
    public double EstimatedRateHz => nominalRateHz + slopeFramesPerSecond;

    /// <summary>How far the measured rate sits from the nominal one, in parts per million.</summary>
    public double DriftPpm => slopeFramesPerSecond / nominalRateHz * 1_000_000.0;

    /// <summary>
    /// Whether enough history has accumulated for the estimate to mean anything. Before this, read
    /// nothing into the numbers.
    /// </summary>
    public bool IsSettled { get; private set; }

    /// <summary>
    /// Whether the estimate has left the range where "drift" is a plausible explanation.
    /// </summary>
    /// <remarks>
    /// Says so rather than producing a number, because tracking a nonsense estimate would apply a
    /// nonsense correction. Something other than drift is wrong and a person should hear about it.
    /// </remarks>
    public bool IsDiverging { get; private set; }

    /// <summary>Observations currently inside the window.</summary>
    public int SampleCount => count;

    /// <summary>Most recently observed fill.</summary>
    public int LastFillFrames { get; private set; }

    /// <summary>
    /// Records one observation. Call on a timer, never from the audio thread.
    /// </summary>
    /// <param name="fillFrames">The ring's current fill.</param>
    /// <param name="elapsed">Time since the previous observation.</param>
    public void Observe(int fillFrames, TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks);

        elapsedSeconds += elapsed.TotalSeconds;
        lastIntervalSeconds = elapsed.TotalSeconds;
        LastFillFrames = fillFrames;

        sampleTimes[next] = elapsedSeconds;
        sampleFills[next] = fillFrames;
        next = (next + 1) % capacity;

        if (count < capacity)
        {
            count++;
        }

        UpdateSlope();
    }

    /// <summary>
    /// Discards all history.
    /// </summary>
    /// <remarks>
    /// For a device that disappeared and came back. Its old fill history describes a stream that
    /// has since stopped, and letting the estimate converge from stale data would apply a
    /// correction for a rate the device is no longer running at.
    /// </remarks>
    public void Reset()
    {
        count = 0;
        next = 0;
        elapsedSeconds = 0.0;
        lastIntervalSeconds = 0.0;
        slopeFramesPerSecond = 0.0;
        IsSettled = false;
        IsDiverging = false;
        LastFillFrames = 0;
    }

    void UpdateSlope()
    {
        double cutoff = elapsedSeconds - windowSeconds;

        double sumTime = 0.0;
        double sumFill = 0.0;
        double sumTimeSquared = 0.0;
        double sumTimeFill = 0.0;
        int used = 0;
        double oldest = double.MaxValue;

        for (int index = 0; index < count; index++)
        {
            double time = sampleTimes[index];

            if (time < cutoff)
            {
                continue;
            }

            double fill = sampleFills[index];

            sumTime += time;
            sumFill += fill;
            sumTimeSquared += time * time;
            sumTimeFill += time * fill;
            used++;

            if (time < oldest)
            {
                oldest = time;
            }
        }

        // Two points define a line but not a trend, and a span of nearly zero makes the divisor
        // vanish. Either way there is nothing to say yet.
        if (used < 3)
        {
            return;
        }

        double divisor = (used * sumTimeSquared) - (sumTime * sumTime);

        if (Math.Abs(divisor) < double.Epsilon)
        {
            return;
        }

        slopeFramesPerSecond = ((used * sumTimeFill) - (sumTime * sumFill)) / divisor;

        // Observations land at discrete times, so the oldest one still inside the window is always
        // a little newer than the cutoff - by up to one interval. Demanding the span reach the full
        // window therefore never succeeds unless a sample happens to land exactly on the boundary,
        // which is a floating-point coincidence rather than a property worth depending on.
        double span = elapsedSeconds - oldest;
        IsSettled = elapsedSeconds >= windowSeconds && span >= windowSeconds - lastIntervalSeconds;

        if (!IsSettled)
        {
            return;
        }

        // Either the rate itself is implausible, or the fill has walked well past the band the
        // servo is meant to hold it in. Both mean the explanation is not drift.
        bool implausibleRate = Math.Abs(DriftPpm) > MaxPlausiblePpm;
        bool fillHasRunAway = targetFillFrames > 0 && Math.Abs(LastFillFrames - targetFillFrames) > targetFillFrames;

        IsDiverging = implausibleRate || fillHasRunAway;
    }
}
