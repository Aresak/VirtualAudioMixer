using Vam.Engine.Devices.Clock;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-016. Fully testable without hardware, and the task says to exploit that - this is the one
/// part of the drift chain that can be genuinely understood before touching a device.
/// </summary>
public class DriftEstimatorTests
{
    const int SampleRate = 48000;
    const int TargetFillFrames = 1024;

    static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.0)]
    [InlineData(-29.0)]
    [InlineData(50.0)]
    [InlineData(-67.0)]
    [Trait("Category", TestCategories.Unit)]
    public void AKnownOffsetIsEstimatedWithinOnePartPerMillion(double actualPpm)
    {
        DriftEstimator estimator = new(SampleRate, TargetFillFrames, Window);

        Simulate(estimator, actualPpm, TimeSpan.FromSeconds(120), jitterFrames: 0);

        Assert.True(estimator.IsSettled);
        Assert.False(estimator.IsDiverging);
        Assert.Equal(actualPpm, estimator.DriftPpm, 1.0);
        Assert.Equal(SampleRate * (1.0 + (actualPpm / 1_000_000.0)), estimator.EstimatedRateHz, 0.1);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void JitterDoesNotMoveTheEstimate()
    {
        const double actualPpm = 40.0;

        // Five milliseconds of jitter is 240 frames at 48 kHz, which dwarfs the drift being
        // measured: 40 ppm moves the fill by less than two frames a second. An estimator that
        // differenced two readings would chase this and make things worse than doing nothing.
        int jitterFrames = (int)(0.005 * SampleRate);

        // The tolerance is derived, not chosen to make this pass. For a least-squares slope over
        // N evenly spaced observations spanning T seconds, with per-observation noise of standard
        // deviation s, the slope's standard error is s * sqrt(12) / (sqrt(N) * T). Uniform jitter
        // of +/-240 frames gives s = 480/sqrt(12) = 138.6 frames, and at 10 Hz over 120 s that is
        // N = 1200, so the error is about 0.12 frames/second - roughly 2.4 ppm at 48 kHz.
        //
        // Five ppm is therefore about two standard errors. Wanting materially better than that
        // means a longer window, and nothing else: it is a property of the arithmetic, not of the
        // implementation.
        const double tolerancePpm = 5.0;

        DriftEstimator estimator = new(SampleRate, TargetFillFrames, TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(100));
        Simulate(estimator, actualPpm, TimeSpan.FromSeconds(300), jitterFrames, TimeSpan.FromMilliseconds(100));

        Assert.True(estimator.IsSettled);
        Assert.False(estimator.IsDiverging);
        Assert.Equal(actualPpm, estimator.DriftPpm, tolerancePpm);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnImplausibleRateSetsIsDivergingRatherThanProducingANumber()
    {
        // Two thousand ppm is not drift. It is a wrong sample rate, a stalled thread, or a device
        // lying about itself - and tracking it would apply a correction for a fiction.
        DriftEstimator estimator = new(SampleRate, TargetFillFrames, Window);

        Simulate(estimator, actualPpm: 2000.0, TimeSpan.FromSeconds(120), jitterFrames: 0);

        Assert.True(estimator.IsSettled);
        Assert.True(estimator.IsDiverging);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NothingIsClaimedBeforeTheWindowHasFilled()
    {
        DriftEstimator estimator = new(SampleRate, TargetFillFrames, Window);

        Simulate(estimator, actualPpm: 50.0, TimeSpan.FromSeconds(10), jitterFrames: 0);

        Assert.False(estimator.IsSettled);
        Assert.False(estimator.IsDiverging);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ResetDiscardsHistoryFromBeforeADeviceLeft()
    {
        DriftEstimator estimator = new(SampleRate, TargetFillFrames, Window);
        Simulate(estimator, actualPpm: 50.0, TimeSpan.FromSeconds(120), jitterFrames: 0);
        Assert.True(estimator.IsSettled);

        estimator.Reset();

        Assert.False(estimator.IsSettled);
        Assert.Equal(0, estimator.SampleCount);
        Assert.Equal(0.0, estimator.DriftPpm);

        // Converges from scratch on the device's new rate rather than dragging the old one along.
        Simulate(estimator, actualPpm: -30.0, TimeSpan.FromSeconds(120), jitterFrames: 0);

        Assert.True(estimator.IsSettled);
        Assert.Equal(-30.0, estimator.DriftPpm, 1.0);
    }

    /// <summary>
    /// Runs the fill a real ring would have. The consumer is the master clock, so it takes exactly
    /// the nominal rate; whatever the producer does differently shows up as the fill drifting.
    /// </summary>
    static void Simulate(
        DriftEstimator estimator,
        double actualPpm,
        TimeSpan duration,
        int jitterFrames,
        TimeSpan? interval = null)
    {
        TimeSpan step = interval ?? Interval;
        double producerRate = SampleRate * (1.0 + (actualPpm / 1_000_000.0));
        double drift = producerRate - SampleRate;
        double fill = TargetFillFrames;
        double elapsed = 0.0;

        // Fixed seed: a gate that fails one run in ten gets disabled within a week.
        Random jitter = new(20260829);

        while (elapsed < duration.TotalSeconds)
        {
            fill += drift * step.TotalSeconds;
            elapsed += step.TotalSeconds;

            int observed = (int)Math.Round(fill);

            if (jitterFrames > 0)
            {
                observed += jitter.Next(-jitterFrames, jitterFrames + 1);
            }

            estimator.Observe(observed, step);
        }
    }
}
