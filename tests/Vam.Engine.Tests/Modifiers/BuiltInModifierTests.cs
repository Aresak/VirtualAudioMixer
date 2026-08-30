using Vam.Engine.Modifiers.BuiltIn;
using Vam.TestKit.Allocations;
using Vam.TestKit.Dsp;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Modifiers;

/// <summary>
/// EPIC-05. Each modifier against a known signal with a known answer.
/// </summary>
/// <remarks>
/// Against arithmetic rather than against a recording, because a recording tells you something
/// sounds wrong and never which stage did it. The listening judgements — whether the denoise is
/// pleasant, whether the automixer is transparent — need real material and are owed separately.
/// </remarks>
public class BuiltInModifierTests
{
    const int Blocks = 200;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheHighPassLetsSpeechThroughAndStopsRumble()
    {
        SignalHarness harness = new(new HighPassModifier());

        harness.Set("frequency", 80f);
        harness.Set("slope", 24f);

        float passed = harness.RunTone(1000.0, 0.5f, Blocks);

        harness = new SignalHarness(new HighPassModifier());
        harness.Set("frequency", 80f);
        harness.Set("slope", 24f);

        float stopped = harness.RunTone(20.0, 0.5f, Blocks);

        // A kilohertz is a voice and passes essentially untouched. Twenty hertz is the building,
        // two octaves below the corner, and at twenty-four decibels an octave that is a long way
        // down.
        Assert.Equal(0.5f, passed, 0.02f);
        Assert.True(stopped < 0.05f, $"Twenty hertz came through at {stopped:F4}, which is not stopped.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheGateClosesOnSilenceAndOpensOnSpeech()
    {
        GateModifier gate = new();
        SignalHarness harness = new(gate);

        harness.Set("threshold", -40f);
        harness.Set("depth", -60f);
        harness.Set("hold", 10f);

        harness.RunTone(500.0, 0.0005f, Blocks);

        Assert.False(gate.IsOpen);

        float loud = harness.RunTone(500.0, 0.5f, Blocks);

        Assert.True(gate.IsOpen);
        Assert.Equal(0.5f, loud, 0.02f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheCompressorPushesBackAboveItsThresholdAndLeavesQuietAudioAlone()
    {
        SignalHarness quiet = new(new CompressorModifier());

        quiet.Set("threshold", -20f);
        quiet.Set("ratio", 4f);
        quiet.Set("knee", 0f);
        quiet.Set("attack", 1f);

        // Well under the threshold, so nothing should happen to it at all.
        float below = quiet.RunTone(1000.0, 0.02f, Blocks);

        Assert.Equal(0.02f, below, 0.002f);

        SignalHarness loud = new(new CompressorModifier());

        loud.Set("threshold", -20f);
        loud.Set("ratio", 4f);
        loud.Set("knee", 0f);
        loud.Set("attack", 1f);

        float above = loud.RunTone(1000.0, 0.5f, Blocks);

        // Twenty decibels over at four to one comes back to about five decibels over, which is
        // roughly 0.18 rather than 0.5. Asserting a band rather than a number, because the envelope
        // follower's exact settling is not the thing under test.
        Assert.InRange(above, 0.12f, 0.25f);
        Assert.True(loud.Telemetry.GainReductionDb < -6f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheEqualiserLiftsTheBandItIsPointedAt()
    {
        SignalHarness harness = new(new EqualiserModifier());

        harness.Set("band3.frequency", 1000f);
        harness.Set("band3.gain", 12f);
        harness.Set("band3.q", 2f);

        float lifted = harness.RunTone(1000.0, 0.1f, Blocks);

        SignalHarness untouched = new(new EqualiserModifier());

        untouched.Set("band3.frequency", 1000f);
        untouched.Set("band3.gain", 12f);
        untouched.Set("band3.q", 2f);

        float elsewhere = untouched.RunTone(100.0, 0.1f, Blocks);

        // Twelve decibels is four times. A hundred hertz is more than three octaves away from a
        // bell at Q of two and should be left where it was.
        Assert.Equal(0.4f, lifted, 0.05f);
        Assert.Equal(0.1f, elsewhere, 0.02f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheLimiterHoldsTheCeiling()
    {
        SignalHarness harness = new(new LimiterModifier());

        harness.Set("ceiling", -6f);

        float peak = harness.RunTone(200.0, 0.9f, Blocks);

        // Minus six decibels is a half. Nothing above the ceiling leaves the stream bus, which is
        // the one promise this modifier exists to keep.
        Assert.True(peak <= 0.51f, $"The limiter let {peak:F4} through against a ceiling of 0.5.");
        Assert.True(harness.Telemetry.GainReductionDb < -3f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AdaptiveGainBringsAQuietTalkerUp()
    {
        AdaptiveGainModifier gain = new();
        SignalHarness harness = new(gain, frameCount: 480);

        harness.Set("target", -23f);
        harness.Set("maximum", 18f);
        harness.Set("response", 4f);

        // Roughly forty decibels below full scale, which is about where the real council recording
        // sits. Run for long enough to fill the three-second window and then some.
        harness.RunTone(500.0, 0.01f, 2000);

        Assert.True(
            gain.AppliedGainDb > 6f,
            $"A very quiet talker was only lifted by {gain.AppliedGainDb:F1} dB.");

        Assert.True(gain.AppliedGainDb <= 18f, "It exceeded the ceiling it was given.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AdaptiveGainDoesNotTurnUpASilentRoom()
    {
        AdaptiveGainModifier gain = new();
        SignalHarness harness = new(gain, frameCount: 480);

        harness.Set("gate", -50f);

        harness.RunTone(500.0, 0.0f, 2000);

        // The failure mode every automatic gain control is remembered for: a pause measures as very
        // quiet, and answering that by turning the room up to conversational level.
        Assert.Equal(0f, gain.AppliedGainDb, 0.01f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void DenoiseLeavesAToneRecognisableAndReportsItsBackendHonestly()
    {
        DenoiseModifier denoise = new();
        SignalHarness harness = new(denoise, frameCount: 512);

        harness.Set("strength", 0.7f);

        float peak = harness.RunTone(1000.0, 0.3f, 100);

        // Spectral subtraction is not transparent and is not claimed to be. What it must not do is
        // remove a sustained tone, which is what it looks like when the noise estimate has learned
        // the signal instead of the background.
        Assert.True(peak > 0.15f, $"A steady tone came out at {peak:F4}, so the estimate ate the signal.");

        // Said out loud, because B4 is open until RNNoise is in and the console must not imply
        // otherwise.
        Assert.Contains("managed", denoise.BackendName, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("highpass")]
    [InlineData("gate")]
    [InlineData("compressor")]
    [InlineData("equaliser")]
    [InlineData("limiter")]
    [InlineData("adaptivegain")]
    [InlineData("denoise")]
    [Trait("Category", TestCategories.Unit)]
    public void NoModifierAllocatesWhileProcessing(string which)
    {
        SignalHarness harness = new(Create(which), frameCount: 512);

        harness.Fill(1000.0, 0.3f);

        // Warmed hard before measuring: several of these design coefficients or fill a window on
        // their first blocks, which is work that happens once and would otherwise be reported as if
        // it repeated.
        for (int block = 0; block < 256; block++)
        {
            harness.Process();
        }

        AllocationAssert.None(harness, static rig => rig.Process());
    }

    static Vam.Modifiers.Abstractions.Modifier Create(string which) => which switch
    {
        "highpass" => new HighPassModifier(),
        "gate" => new GateModifier(),
        "compressor" => new CompressorModifier(),
        "equaliser" => new EqualiserModifier(),
        "limiter" => new LimiterModifier(),
        "adaptivegain" => new AdaptiveGainModifier(),
        "denoise" => new DenoiseModifier(),
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, "Unknown modifier.")
    };
}
