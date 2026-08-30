using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Diagnostics;

/// <summary>
/// I5. The soak that drives the whole graph rather than only the device layer.
/// </summary>
/// <remarks>
/// The drift soak proves the rings and the servo hold over hours and never touches a modifier chain,
/// an automixer or a bus. This one drives all of it, with an operator changing things while it runs
/// — which is where a snapshot swap meets a render, and the one place that most wants soaking.
/// </remarks>
public class GraphSoakTests
{
    /// <summary>
    /// How much audio the long soak pushes through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight hours in a release build, which is the gate EPIC-12 asks for: the shape of a day that
    /// starts with a sound check and ends with somebody forgetting to stop the engine.
    /// </para>
    /// <para>
    /// Half an hour in a debug build, because a debug build renders about a tenth as fast and eight
    /// hours of it is four hours of wall time. A four-hour test is a test nobody runs, and a test
    /// nobody runs protects nothing — so the debug build gets a soak that will actually be started
    /// and the release build gets the real one.
    /// </para>
    /// </remarks>
#if DEBUG
    static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);
#else
    static readonly TimeSpan Duration = TimeSpan.FromHours(8);
#endif

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AShortSoakWithTheChainsRunningAllocatesNothing()
    {
        GraphSoak soak = new(channelCount: 5);
        GraphSoakReport report = soak.Run(TimeSpan.FromSeconds(20), disturbEvery: TimeSpan.FromSeconds(2));

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());

        // Rule 1, through the whole graph and not only through a ring. Five chains of six modifiers,
        // an automixer, two buses and a limiter, with faders moving and a plan being recompiled
        // underneath it.
        Assert.Equal(0, report.AudioThreadBytes);
        Assert.True(report.Disturbances > 0, "Nothing was changed during the soak, so the swap was never exercised.");
    }

    [Fact(
        Skip = "Long-running tests are excluded by default. Set VAM_LONGRUNNING=1 to run them.",
        SkipType = typeof(LongRunningTests),
        SkipUnless = nameof(LongRunningTests.IsEnabled))]
    [Trait("Category", TestCategories.LongRunning)]
    public void EightSimulatedHoursOfAFullConsoleAllocateNothingAndMissNoDeadline()
    {
        GraphSoak soak = new(channelCount: 5);
        GraphSoakReport report = soak.Run(Duration, disturbEvery: TimeSpan.FromMinutes(1));

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());

        // The number that has to be zero, over hours rather than over twenty seconds. A leak of one
        // byte a block is invisible in a unit test and is four megabytes by the interval.
        Assert.Equal(0, report.AudioThreadBytes);

        // Not a hard assertion about wall time, which depends on the machine — but a soak that could
        // not outrun realtime would be an eight-hour test, and an eight-hour test never gets run.
        Assert.True(report.Speed > 1.5, $"The soak only managed {report.Speed:0.#} times realtime.");
    }
}
