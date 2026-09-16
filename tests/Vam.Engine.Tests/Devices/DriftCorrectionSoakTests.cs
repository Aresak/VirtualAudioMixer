using Vam.Engine.Devices;
using Vam.TestKit.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-017's first two acceptance criteria: two devices drifting in opposite directions stay in
/// sync across a simulated eight hours, with the buffer fill inside its band and showing no trend.
/// </summary>
/// <remarks>
/// <para>
/// Eight hours because that is the shape of the problem. Forty parts per million is a fifth of a
/// frame every five milliseconds - invisible for a minute, and fifty-five thousand frames by the
/// end of a working day, which is thirteen times everything the ring can hold. A five-minute test
/// cannot tell a working servo from a broken one.
/// </para>
/// <para>
/// This does not close EPIC-02. VAM-022 wants four real devices, eight real hours and a physical
/// unplug, and says it cannot be shortened or simulated. This proves the loop; that proves the room.
/// </para>
/// </remarks>
public class DriftCorrectionSoakTests
{
    const int BlockFrames = 120;
    const int TargetFillFrames = 1024;
    const double DriftPpm = 40.0;

    /// <summary>
    /// How far the fill may wander. A quarter of the setpoint either way: beyond that the device is
    /// contributing a different amount of latency than it was configured for, which is audible as a
    /// changed relationship between the microphones rather than as a click.
    /// </summary>
    const double BandFraction = 0.25;

    /// <summary>
    /// The trend that would matter. A slope big enough to walk the fill out of its band inside one
    /// eight-hour session is a failure however comfortable the fill looks at any single moment.
    /// </summary>
    const double MaximumSlopeFramesPerSecond = 0.005;

    static readonly TimeSpan CorrectionInterval = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(10);
    static readonly TimeSpan MeasuredTime = TimeSpan.FromHours(8);
    static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    [Fact(
        Skip = "Long-running tests are excluded by default. Set VAM_LONGRUNNING=1 to run them.",
        SkipType = typeof(LongRunningTests),
        SkipUnless = nameof(LongRunningTests.IsEnabled))]
    [Trait("Category", TestCategories.LongRunning)]
    public void EightSimulatedHoursOfOppositeDriftStaysInBandAndShowsNoTrend()
    {
        using DriftSimulation simulation = new(BlockFrames, TargetFillFrames, CorrectionInterval);

        DeviceInputChannel fast = simulation.AddDevice("Mayor 180 degrees", DriftPpm);
        DeviceInputChannel slow = simulation.AddDevice("Lectern", -DriftPpm);

        simulation.Prime();

        // The first minutes are the servo discovering the drift, which is a transient rather than a
        // steady state. Measuring through it would report the startup as if it were a trend.
        simulation.Run(SettleTime);

        long overrunsAtStart = TotalOverruns(simulation);
        long underrunsAtStart = TotalUnderruns(simulation);

        List<int> fastFills = [];
        List<int> slowFills = [];
        TimeSpan nextSample = simulation.Elapsed;
        TimeSpan until = simulation.Elapsed + MeasuredTime;

        while (simulation.Elapsed < until)
        {
            simulation.Step();

            if (simulation.Elapsed < nextSample)
            {
                continue;
            }

            fastFills.Add(fast.FillFrames);
            slowFills.Add(slow.FillFrames);
            nextSample += SampleInterval;
        }

        AssertHeldItsBand(fastFills, "the fast device");
        AssertHeldItsBand(slowFills, "the slow device");

        // A servo that did nothing would also produce a flat trend - for about twenty minutes, until
        // the rings hit their ends. What proves it worked is that each correction found the drift it
        // was fighting, and that the two found opposite ones.
        Assert.Equal(DriftPpm, fast.GetTelemetry().DriftPpm, 2.0);
        Assert.Equal(-DriftPpm, slow.GetTelemetry().DriftPpm, 2.0);

        Assert.Equal(overrunsAtStart, TotalOverruns(simulation));
        Assert.Equal(underrunsAtStart, TotalUnderruns(simulation));

        Assert.Equal(0, fast.ClampCount);
        Assert.Equal(0, slow.ClampCount);
    }

    static void AssertHeldItsBand(List<int> fills, string which)
    {
        Assert.NotEmpty(fills);

        double lowest = fills.Min();
        double highest = fills.Max();
        double margin = TargetFillFrames * BandFraction;

        Assert.True(
            lowest >= TargetFillFrames - margin && highest <= TargetFillFrames + margin,
            $"Fill for {which} ranged {lowest} to {highest}, outside the band "
            + $"{TargetFillFrames - margin} to {TargetFillFrames + margin}.");

        // The second half only. The first is still shedding whatever the settle period left behind,
        // and a trend that has already stopped is not a trend.
        double slope = SlopeOverSecondHalf(fills);

        Assert.True(
            Math.Abs(slope) <= MaximumSlopeFramesPerSecond,
            $"Fill for {which} trended {slope:F6} frames per second, which walks it "
            + $"{Math.Abs(slope) * MeasuredTime.TotalSeconds:F0} frames across a session.");
    }

    static double SlopeOverSecondHalf(List<int> fills)
    {
        int from = fills.Count / 2;
        int used = fills.Count - from;

        double sumTime = 0.0;
        double sumFill = 0.0;
        double sumTimeSquared = 0.0;
        double sumTimeFill = 0.0;

        for (int index = 0; index < used; index++)
        {
            double time = index * SampleInterval.TotalSeconds;
            double fill = fills[from + index];

            sumTime += time;
            sumFill += fill;
            sumTimeSquared += time * time;
            sumTimeFill += time * fill;
        }

        double divisor = (used * sumTimeSquared) - (sumTime * sumTime);

        return Math.Abs(divisor) < double.Epsilon
            ? 0.0
            : ((used * sumTimeFill) - (sumTime * sumFill)) / divisor;
    }

    static long TotalOverruns(DriftSimulation simulation)
    {
        long total = 0;

        foreach (DeviceInputChannel channel in simulation.Channels)
        {
            total += channel.GetTelemetry().OverrunCount;
        }

        return total;
    }

    static long TotalUnderruns(DriftSimulation simulation)
    {
        long total = 0;

        foreach (DeviceInputChannel channel in simulation.Channels)
        {
            total += channel.GetTelemetry().UnderrunCount;
        }

        return total;
    }
}
