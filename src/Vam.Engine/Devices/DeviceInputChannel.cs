using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Devices.Clock;

namespace Vam.Engine.Devices;

/// <summary>
/// One capture device's path into the mix graph: its ring buffer, its drift estimate, its servo
/// and its resampler.
/// </summary>
/// <remarks>
/// <para>
/// Three threads meet here and each owns a different part of it.
/// </para>
/// <list type="bullet">
/// <item>
/// The <b>device thread</b> calls <see cref="Write"/>, which is one ring write and nothing else.
/// Inside the audio path.
/// </item>
/// <item>
/// The <b>mix thread</b> calls <see cref="Pull"/>, which reads the ring through the resampler at
/// whatever ratio is currently set. Inside the audio path.
/// </item>
/// <item>
/// The <b>control thread</b> calls <see cref="UpdateCorrection"/> on a timer, which is where the
/// estimator and the servo run and where the ratio is changed. Outside the audio path, and the
/// audio thread never waits for it.
/// </item>
/// </list>
/// <para>
/// The ratio is written by the control thread and read by the mix thread with no synchronisation
/// beyond the fact that a correctly aligned 64-bit write is atomic on every runtime this ships on.
/// The mix thread therefore sees either the old ratio or the new one, never a mixture, and either
/// is a legitimate answer - the whole point of the resampler carrying its fractional position
/// across calls is that changing the ratio mid-stream produces no discontinuity.
/// </para>
/// </remarks>
public sealed class DeviceInputChannel
{
    const double PartsPerMillion = 1_000_000.0;
    const double Percent = 100.0;

    /// <summary>
    /// Slack above the largest block, so one pull normally reaches the resampler in a single call.
    /// The ratio never leaves +/-500 ppm, so a block is never short by more than a frame or two.
    /// </summary>
    const int InputHeadroomFrames = 8;

    readonly AudioRingBuffer ring;
    readonly DriftEstimator estimator;
    readonly DriftResampler resampler;
    readonly FillServo servo;
    readonly ILogger<DeviceInputChannel> logger;
    readonly float[] pending;
    readonly int channelCount;
    readonly int nominalRateHz;
    readonly int maxInputFrames;

    int pendingFrames;
    int state = (int)DeviceStreamState.Stopped;
    long underrunCount;
    long loggedClampCount;
    double measuredRateHz;
    double driftPpm;
    double ratio = 1.0;

    /// <summary>Builds a channel and everything it owns.</summary>
    /// <param name="deviceId">Which device this carries. Used for identity and for log lines.</param>
    /// <param name="options">Rates, sizes and the fill setpoint.</param>
    /// <param name="logger">Where a clamped correction is reported. Never called from the audio path.</param>
    public DeviceInputChannel(
        AudioDeviceId deviceId,
        DeviceInputChannelOptions options,
        ILogger<DeviceInputChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.NominalSampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ChannelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BlockFrames, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RingCapacityFrames, options.BlockFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(options.TargetFillFrames);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(options.TargetFillFrames, options.RingCapacityFrames);

        DeviceId = deviceId;
        this.logger = logger;

        channelCount = options.ChannelCount;
        nominalRateHz = options.NominalSampleRate;
        maxInputFrames = options.BlockFrames + InputHeadroomFrames;

        ring = new AudioRingBuffer(options.RingCapacityFrames, channelCount);
        resampler = new DriftResampler(channelCount, maxInputFrames);
        servo = new FillServo(nominalRateHz, options.TargetFillFrames);

        estimator = new DriftEstimator(
            nominalRateHz,
            options.TargetFillFrames,
            options.EstimatorWindow,
            options.CorrectionInterval);

        pending = new float[maxInputFrames * channelCount];

        // Until the estimator has anything to say, the honest answer is the rate the device claims
        // rather than zero - a strip header reading 0 Hz before the first timer tick is a bug report.
        measuredRateHz = nominalRateHz;
    }

    /// <summary>Which device this channel carries.</summary>
    public AudioDeviceId DeviceId { get; }

    /// <summary>Channels interleaved in every buffer.</summary>
    public int ChannelCount => channelCount;

    /// <summary>
    /// What the stream is doing. Set by the supervisor off the audio path; read from anywhere.
    /// </summary>
    public DeviceStreamState State
    {
        get => (DeviceStreamState)Volatile.Read(ref state);
        set => Volatile.Write(ref state, (int)value);
    }

    /// <summary>
    /// Frames buffered between the device and the mix graph.
    /// </summary>
    /// <remarks>
    /// The ring plus whatever the pull side has read out of it but not yet consumed. Both count:
    /// the servo is holding the total latency at its setpoint, and reporting only the ring would
    /// tell it a block's worth of lie.
    /// </remarks>
    public int FillFrames => ring.FillFrames + Volatile.Read(ref pendingFrames);

    /// <summary>Input frames consumed per output frame. The correction currently being applied.</summary>
    public double Ratio => Volatile.Read(ref ratio);

    /// <summary>Corrections applied since the channel was built.</summary>
    public long CorrectionCount => servo.CorrectionCount;

    /// <summary>Times the correction hit its limit, counted per episode rather than per update.</summary>
    public long ClampCount => servo.ClampCount;

    /// <summary>Whether the servo is currently asking for more correction than it may apply.</summary>
    public bool IsClamping => servo.IsClamping;

    /// <summary>
    /// Takes one buffer from the device.
    /// </summary>
    /// <remarks>
    /// Shaped to be handed straight to <see cref="ICaptureStream.Start"/> as the capture callback.
    /// Inside the audio path: one ring write, and a counter if it would not fit.
    /// </remarks>
    /// <param name="samples">Interleaved samples, valid only for this call.</param>
    /// <param name="frameCount">Frames in the buffer.</param>
    public void Write(ReadOnlySpan<float> samples, int frameCount) =>
        ring.TryWrite(samples[..(frameCount * channelCount)]);

    /// <summary>
    /// Produces the mix graph's next block, resampled onto the master clock.
    /// </summary>
    /// <remarks>
    /// Inside the audio path. Allocates nothing, takes no lock, and never waits for the device: a
    /// device that has stopped delivering produces silence and a counter, not a stall.
    /// </remarks>
    /// <param name="destination">Interleaved output, a whole number of frames.</param>
    /// <returns>
    /// Frames actually produced. Anything short of what was asked for has been filled with silence,
    /// so the caller always receives a complete block.
    /// </returns>
    public int Pull(Span<float> destination)
    {
        int wanted = destination.Length / channelCount;
        int produced = 0;

        while (produced < wanted)
        {
            TopUpPending();

            if (pendingFrames == 0)
            {
                break;
            }

            resampler.Process(
                pending.AsSpan(0, pendingFrames * channelCount),
                destination[(produced * channelCount)..],
                out int consumed,
                out int justProduced);

            DropConsumed(consumed);

            if (justProduced == 0)
            {
                break;
            }

            produced += justProduced;
        }

        if (produced < wanted)
        {
            destination[(produced * channelCount)..].Clear();
            underrunCount += wanted - produced;
        }

        return produced;
    }

    /// <summary>
    /// Runs the estimate and the servo, and applies the result to the resampler.
    /// </summary>
    /// <remarks>
    /// Control thread, on a timer. Outside the audio path, and deliberately so: this is where the
    /// arithmetic, the clamping and the one log line live, none of which may happen on a callback.
    /// </remarks>
    /// <param name="elapsed">Time since the previous call.</param>
    /// <returns>The ratio now in force.</returns>
    public double UpdateCorrection(TimeSpan elapsed)
    {
        int fill = FillFrames;

        estimator.Observe(fill, elapsed);

        double correctionPpm = servo.Update(fill, elapsed.TotalSeconds);

        // Clamped against the resampler's own limit as well as the servo's. The two figures agree
        // today, and a rounding error between them must not be able to throw inside a timer tick.
        double corrected = Math.Clamp(
            1.0 + (correctionPpm / PartsPerMillion),
            1.0 - DriftResampler.MaxRatioDeviation,
            1.0 + DriftResampler.MaxRatioDeviation);

        resampler.Ratio = corrected;

        double measured = MeasureDeviceRate(corrected);

        Volatile.Write(ref ratio, corrected);
        Volatile.Write(ref measuredRateHz, measured);
        Volatile.Write(ref driftPpm, (measured - nominalRateHz) / nominalRateHz * PartsPerMillion);

        ReportClamping();

        return corrected;
    }

    /// <summary>
    /// Assembles this channel's telemetry.
    /// </summary>
    /// <remarks>
    /// Safe from any thread and cheap enough to poll at meter rate. Values may be up to one
    /// correction interval stale, which is documented on <see cref="DeviceTelemetry"/> rather than
    /// fixed - a lock here would put the audio thread behind a display.
    /// </remarks>
    /// <returns>The current figures.</returns>
    public DeviceTelemetry GetTelemetry() =>
        new(
            nominalRateHz,
            Volatile.Read(ref measuredRateHz),
            Volatile.Read(ref driftPpm),
            Volatile.Read(ref ratio),
            (double)FillFrames / ring.CapacityFrames * Percent,
            ring.OverrunCount,
            Volatile.Read(ref underrunCount),
            State);

    /// <summary>
    /// Discards everything buffered and returns the correction to rest.
    /// </summary>
    /// <remarks>
    /// <b>Not safe while the stream is running.</b> For a device that disappeared and came back:
    /// its ring holds audio from before it left, its drift history describes a stream that has
    /// stopped, and its integral describes a rate error the returning device may not have.
    /// </remarks>
    public void Reset()
    {
        ring.Reset();
        estimator.Reset();
        resampler.Reset();
        servo.Reset();

        pendingFrames = 0;
        underrunCount = 0;
        loggedClampCount = 0;
        measuredRateHz = nominalRateHz;
        driftPpm = 0.0;
        ratio = 1.0;
    }

    double MeasureDeviceRate(double currentRatio)
    {
        // The estimator reads a device's rate off the slope of its ring fill, which is exact right
        // up until a servo starts holding that fill flat - and then the slope is zero however fast
        // the device is really running. Taking the estimate at face value here would leave the strip
        // header reporting the nominal rate forever, which is the one number it exists not to show.
        //
        // What the fill is doing still says something, just not the whole thing. The device produces
        // whatever we consume plus whatever the ring is gaining: consumption is the nominal rate
        // scaled by the ratio, and the ring's gain is the slope the estimator has already fitted.
        // Closed or open loop, the two together are the device's rate.
        double consumedRateHz = nominalRateHz * currentRatio;
        double ringGainFramesPerSecond = estimator.EstimatedRateHz - nominalRateHz;

        return consumedRateHz + ringGainFramesPerSecond;
    }

    void TopUpPending()
    {
        // Only ever asks the ring for what it actually holds, so a short read is impossible and the
        // ring's own underrun counter keeps meaning what it says. Coming up short is this channel's
        // business to count, in Pull, where the silence is inserted.
        int want = Math.Min(maxInputFrames - pendingFrames, ring.FillFrames);

        if (want <= 0)
        {
            return;
        }

        int read = ring.Read(pending.AsSpan(pendingFrames * channelCount, want * channelCount));

        Volatile.Write(ref pendingFrames, pendingFrames + read);
    }

    void DropConsumed(int consumed)
    {
        if (consumed <= 0)
        {
            return;
        }

        int remaining = pendingFrames - consumed;

        // The resampler's filter window reaches back into frames it has already consumed, and it
        // keeps that history itself. What is shifted down here is only what it has not yet used.
        if (remaining > 0)
        {
            pending.AsSpan(consumed * channelCount, remaining * channelCount).CopyTo(pending);
        }

        Volatile.Write(ref pendingFrames, remaining);
    }

    void ReportClamping()
    {
        if (servo.ClampCount == loggedClampCount)
        {
            return;
        }

        loggedClampCount = servo.ClampCount;

        logger.LogWarning(
            "Drift correction for {DeviceId} reached its {LimitPpm} ppm limit with the ring {FillFrames} frames "
            + "against a target of {TargetFillFrames}. Something other than clock drift is wrong.",
            DeviceId,
            FillServo.MaxCorrectionPpm,
            FillFrames,
            servo.TargetFillFrames);
    }
}
