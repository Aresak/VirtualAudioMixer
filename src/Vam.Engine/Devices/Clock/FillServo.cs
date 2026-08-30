namespace Vam.Engine.Devices.Clock;

/// <summary>
/// Holds a device's ring buffer at its target fill by trimming the resampler's ratio.
/// </summary>
/// <remarks>
/// <para>
/// This is the correction half of clock discipline, and it is deliberately not the measurement
/// half. <see cref="DriftEstimator"/> reports what a device's rate <i>is</i>, for the strip header
/// and the diagnostics column. Driving the resampler from that number directly would be open loop:
/// a 0.1 ppm error in the estimate is 17 frames an hour, which is nothing for three hours and an
/// underrun on the fourth. A servo on fill is self-correcting instead, and survives a device that
/// lies about its own clock.
/// </para>
/// <para>
/// The plant is an integrator - fill accumulates the difference between the device's rate and
/// ours - so proportional control alone would leave a standing offset and park the ring wherever
/// it happened to drift to. The integral term is what actually returns it to the setpoint.
/// </para>
/// <para>
/// Control thread only, on a timer. It is outside the audio path: it reads a fill level and
/// computes a number, and the audio thread never waits for it.
/// </para>
/// </remarks>
public sealed class FillServo
{
    /// <summary>
    /// Widest correction the servo will ever ask for. Beyond this the explanation is not drift,
    /// and applying more would turn a wrong measurement into wrecked audio.
    /// </summary>
    public const double MaxCorrectionPpm = 500.0;

    const double PartsPerMillion = 1_000_000.0;

    /// <summary>
    /// Closed-loop bandwidth. Twenty seconds of time constant: drift moves over minutes, so a
    /// faster loop would spend its time chasing the jitter in the fill reading instead.
    /// </summary>
    const double NaturalFrequencyRadiansPerSecond = 0.05;

    /// <summary>
    /// Critically damped. Overshoot here is not cosmetic - an overshoot towards empty is an
    /// underrun that the correction itself caused, which is worse than the drift it was fixing.
    /// </summary>
    const double DampingRatio = 1.0;

    /// <summary>
    /// How fast the correction is allowed to move. A ratio that steps is a discontinuity in the
    /// audio; a ratio that slides is not, and fifty parts per million a second is a sixth of a cent
    /// per second - inaudible by a wide margin.
    /// </summary>
    /// <remarks>
    /// This is a guard against a discontinuity, <b>not</b> the loop's dominant dynamic, and the
    /// difference matters. A limiter slow enough to govern the response cannot decelerate in time:
    /// the loop reaches its authority, the fill returns to target, and the correction is still
    /// draining the ring on the way past because it needs a hundred seconds to come back down. That
    /// undershoot is an underrun the correction itself caused. The limit therefore sits well above
    /// the bandwidth set by <see cref="NaturalFrequencyRadiansPerSecond"/>, so the loop decides the
    /// shape of the response and this only forbids a jump.
    /// </remarks>
    const double MaxSlewPpmPerSecond = 50.0;

    readonly int targetFillFrames;
    readonly double proportionalPpmPerFrame;
    readonly double integralPpmPerFrameSecond;

    double integralPpm;
    double correctionPpm;
    bool isClamping;
    long clampCount;
    long correctionCount;

    /// <summary>Creates a servo for one device.</summary>
    /// <param name="nominalRateHz">The rate the device claims. Sets the loop's scaling.</param>
    /// <param name="targetFillFrames">Where the ring is meant to sit.</param>
    public FillServo(int nominalRateHz, int targetFillFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nominalRateHz, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(targetFillFrames);

        this.targetFillFrames = targetFillFrames;

        // Gains come from the plant rather than from taste. Fill integrates (deviceRate - ourRate),
        // so with a PI controller the fill error obeys x" + 2*zeta*wn*x' + wn^2*x = 0 once the
        // nominal rate is divided out. Solving that for the two gains is these two lines, and it is
        // why the constants above are a frequency and a damping ratio rather than two magic numbers.
        proportionalPpmPerFrame =
            2.0 * DampingRatio * NaturalFrequencyRadiansPerSecond * PartsPerMillion / nominalRateHz;

        integralPpmPerFrameSecond =
            NaturalFrequencyRadiansPerSecond * NaturalFrequencyRadiansPerSecond * PartsPerMillion / nominalRateHz;
    }

    /// <summary>Where the ring is meant to sit.</summary>
    public int TargetFillFrames => targetFillFrames;

    /// <summary>The correction currently being applied, in parts per million. Positive drains the ring.</summary>
    public double CorrectionPpm => correctionPpm;

    /// <summary>Whether the loop is currently asking for more than it is allowed to apply.</summary>
    public bool IsClamping => isClamping;

    /// <summary>
    /// Times the loop has hit its limit. Counted per episode rather than per update, so one long
    /// clamp is one event and a caller can log it once rather than every timer tick.
    /// </summary>
    public long ClampCount => clampCount;

    /// <summary>Updates applied since construction or the last <see cref="Reset"/>.</summary>
    public long CorrectionCount => correctionCount;

    /// <summary>
    /// Advances the loop by one observation.
    /// </summary>
    /// <param name="currentFillFrames">The ring's fill right now.</param>
    /// <param name="elapsedSeconds">Time since the previous update.</param>
    /// <returns>The correction to apply, in parts per million.</returns>
    public double Update(int currentFillFrames, double elapsedSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedSeconds);

        double error = currentFillFrames - targetFillFrames;
        double proportionalPpm = proportionalPpmPerFrame * error;
        double wanted = Integrate(error, elapsedSeconds, proportionalPpm) + proportionalPpm;

        RecordClamping(Math.Abs(wanted) > MaxCorrectionPpm);

        double allowed = Math.Clamp(wanted, -MaxCorrectionPpm, MaxCorrectionPpm);
        double maxStep = MaxSlewPpmPerSecond * elapsedSeconds;

        correctionPpm += Math.Clamp(allowed - correctionPpm, -maxStep, maxStep);
        correctionCount++;

        return correctionPpm;
    }

    /// <summary>
    /// Returns the loop to rest.
    /// </summary>
    /// <remarks>
    /// For a device that disappeared and came back. Its accumulated integral describes a rate error
    /// the returning device may not have, and unwinding it would take as long as building it did.
    /// </remarks>
    public void Reset()
    {
        integralPpm = 0.0;
        correctionPpm = 0.0;
        isClamping = false;
        clampCount = 0;
        correctionCount = 0;
    }

    double Integrate(double error, double elapsedSeconds, double proportionalPpm)
    {
        // Anti-windup, and it has to be against the authority the proportional term has not already
        // spent rather than against the output limit on its own. Clamping the integral at the full
        // limit still lets it wind all the way up while the loop is saturated, and an integral that
        // has run to a large value takes exactly as long to unwind: the correction stays hard over
        // long after the fill is back at its setpoint and drives it straight past, towards empty.
        double headroom = Math.Max(MaxCorrectionPpm - Math.Abs(proportionalPpm), 0.0);

        integralPpm = Math.Clamp(
            integralPpm + (integralPpmPerFrameSecond * error * elapsedSeconds),
            -headroom,
            headroom);

        return integralPpm;
    }

    void RecordClamping(bool clamping)
    {
        if (clamping && !isClamping)
        {
            clampCount++;
        }

        isClamping = clamping;
    }
}
