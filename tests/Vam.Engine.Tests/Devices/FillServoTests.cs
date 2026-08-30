using Vam.Engine.Devices.Clock;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-017's correction loop, on its own and away from any audio.
/// </summary>
/// <remarks>
/// The plant is simple enough to write down exactly - a ring's fill is the integral of the
/// difference between the device's rate and the rate we consume at - so the servo can be checked
/// against arithmetic rather than against a recording. What it must do is return the fill to its
/// setpoint and hold it there, not merely stop it moving: a loop that only flattens the trend parks
/// the ring wherever it drifted to, and the ring's job is to be a known amount of latency.
/// </remarks>
public class FillServoTests
{
    const int SampleRate = 48000;
    const int TargetFillFrames = 1024;
    const double PartsPerMillion = 1_000_000.0;

    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(0.0)]
    [InlineData(18.0)]
    [InlineData(-31.0)]
    [InlineData(120.0)]
    [InlineData(-95.0)]
    [Trait("Category", TestCategories.Unit)]
    public void TheCorrectionSettlesOnTheDriftItIsFighting(double driftPpm)
    {
        FillServo servo = new(SampleRate, TargetFillFrames);

        double fill = Simulate(servo, driftPpm, TimeSpan.FromMinutes(30), TargetFillFrames);

        // A device running fast has to be consumed faster, so in steady state the correction is the
        // drift. If it settled anywhere else the fill would still be moving.
        Assert.Equal(driftPpm, servo.CorrectionPpm, 0.5);

        // And the fill is back where it belongs, not merely stationary. This is the assertion that
        // a proportional-only loop fails: it would hold a standing offset forever.
        Assert.Equal(TargetFillFrames, fill, 1.0);
        Assert.False(servo.IsClamping);
        Assert.Equal(0, servo.ClampCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void RecoveringFromAFullRingDoesNotDriveItTowardsEmpty()
    {
        FillServo servo = new(SampleRate, TargetFillFrames);

        double fill = TargetFillFrames * 2.0;
        double lowest = fill;

        for (TimeSpan elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromMinutes(30); elapsed += Interval)
        {
            fill = Advance(servo, fill, driftPpm: 0.0);
            lowest = Math.Min(lowest, fill);
        }

        Assert.Equal(TargetFillFrames, fill, 1.0);

        // Overshoot here is not cosmetic. The loop is correcting a ring that was too full, and an
        // undershoot on the way back is an underrun the correction itself caused - which is a worse
        // outcome than the excess buffering it was sent to remove.
        Assert.True(
            lowest > TargetFillFrames * 0.95,
            $"Fill undershot its target on the way back, reaching {lowest:F1} against a target of {TargetFillFrames}.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ANonsenseFillClampsAndIsCountedOnce()
    {
        FillServo servo = new(SampleRate, TargetFillFrames);

        // Nothing plausible empties a ring this far below its setpoint and holds it there. The loop
        // must refuse to believe it rather than asking for a correction that would wreck the audio.
        for (int update = 0; update < 200; update++)
        {
            servo.Update(0, Interval.TotalSeconds);
        }

        Assert.True(servo.IsClamping);
        Assert.Equal(FillServo.MaxCorrectionPpm, Math.Abs(servo.CorrectionPpm), 0.001);

        // One episode, not two hundred. A caller logging on this counter has to be able to say it
        // once rather than on every timer tick.
        Assert.Equal(1, servo.ClampCount);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheRatioIsNeverStepped()
    {
        FillServo servo = new(SampleRate, TargetFillFrames);

        double previous = servo.CorrectionPpm;
        double largestStep = 0.0;

        // The loop is being asked for far more than it may apply, so every update wants to jump.
        for (int update = 0; update < 400; update++)
        {
            double current = servo.Update(0, Interval.TotalSeconds);
            largestStep = Math.Max(largestStep, Math.Abs(current - previous));
            previous = current;
        }

        // A ratio that steps is a discontinuity in the audio. Fifty parts per million per second is
        // the budget; over a quarter-second update that is a quarter of that.
        Assert.True(
            largestStep <= 12.5 + 1e-9,
            $"The correction moved {largestStep:F4} ppm in one update, which is a step rather than a slide.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ResetReturnsTheLoopToRest()
    {
        FillServo servo = new(SampleRate, TargetFillFrames);

        Simulate(servo, driftPpm: 60.0, TimeSpan.FromMinutes(10), TargetFillFrames);
        Assert.NotEqual(0.0, servo.CorrectionPpm, 0.5);

        servo.Reset();

        Assert.Equal(0.0, servo.CorrectionPpm);
        Assert.Equal(0, servo.CorrectionCount);
        Assert.Equal(0, servo.ClampCount);
        Assert.False(servo.IsClamping);
    }

    /// <summary>
    /// Runs the loop against a ring whose producer is <paramref name="driftPpm"/> away from nominal.
    /// </summary>
    /// <returns>Where the fill ended up.</returns>
    static double Simulate(FillServo servo, double driftPpm, TimeSpan duration, double startingFill)
    {
        double fill = startingFill;

        for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Interval)
        {
            fill = Advance(servo, fill, driftPpm);
        }

        return fill;
    }

    static double Advance(FillServo servo, double fill, double driftPpm)
    {
        double correctionPpm = servo.Update((int)Math.Round(fill), Interval.TotalSeconds);

        // The whole plant, in one line: the ring gains what the device produced and loses what we
        // consumed, and both are the nominal rate scaled by their own parts-per-million offset.
        return fill + (SampleRate * (driftPpm - correctionPpm) / PartsPerMillion * Interval.TotalSeconds);
    }
}
