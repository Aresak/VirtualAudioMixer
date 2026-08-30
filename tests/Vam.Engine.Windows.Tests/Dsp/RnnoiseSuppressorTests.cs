using Vam.Engine.Windows.Dsp;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Dsp;

/// <summary>
/// EPIC-05's native denoise, proven rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// Gated on <see cref="RnnoiseSuppressor.IsAvailable"/> rather than on an environment variable,
/// because the thing that decides whether these can run is whether a 64-bit <c>rnnoise.dll</c> sits
/// beside the test binary — which the test can simply ask, instead of asking an operator to declare
/// it and then be wrong.
/// </para>
/// <para>
/// Nothing here asserts a decibel figure against a published benchmark. RNNoise's quality is its
/// authors' claim and not this project's to re-derive; what these tests establish is that the
/// library VAM loaded is the real one doing real work — that it reduces broadband noise
/// substantially, that strength still means what the interface says it means, and that
/// <c>Process</c> does not allocate, which is the only one of the three that could quietly stop
/// being true.
/// </para>
/// </remarks>
public class RnnoiseSuppressorTests(ITestOutputHelper output)
{
    const string SkipReason = "Needs rnnoise.dll beside the test binary.";

    /// <summary>Long enough for the noise estimate to settle, and then some.</summary>
    const int SettleFrames = 24;

    [Fact(
        Skip = SkipReason,
        SkipType = typeof(RnnoiseSuppressor),
        SkipUnless = nameof(RnnoiseSuppressor.IsAvailable))]
    [Trait("Category", TestCategories.Unit)]
    public void ItTakesBroadbandNoiseDown()
    {
        using RnnoiseSuppressor suppressor = new();

        float[] block = new float[RnnoiseSuppressor.FrameSamples];
        Random noise = new(20260830);

        for (int frame = 0; frame < SettleFrames; frame++)
        {
            Fill(block, noise);
            suppressor.Process(block, 1f);
        }

        Fill(block, noise);

        float before = Rms(block);
        suppressor.Process(block, 1f);
        float after = Rms(block);

        output.WriteLine($"noise in {before:F5}, out {after:F5}, {Ratio(before, after):F1} dB down");

        // Deliberately loose. The point is to catch a library that loaded but is passing audio
        // through untouched - a stub, a wrong export, a model that failed to initialise - not to
        // pin RNNoise to a number that a future version would move.
        Assert.True(after < before * 0.5f, $"Expected a real reduction; got {before} to {after}.");
    }

    [Fact(
        Skip = SkipReason,
        SkipType = typeof(RnnoiseSuppressor),
        SkipUnless = nameof(RnnoiseSuppressor.IsAvailable))]
    [Trait("Category", TestCategories.Unit)]
    public void StrengthOfZeroLeavesTheBlockExactlyAsItWas()
    {
        using RnnoiseSuppressor suppressor = new();

        float[] block = new float[RnnoiseSuppressor.FrameSamples];
        float[] original = new float[block.Length];
        Random noise = new(20260830);

        Fill(block, noise);
        block.CopyTo(original, 0);

        suppressor.Process(block, 0f);

        // Bit-identical, not merely close: an operator who has turned the denoise off is entitled to
        // the samples they started with, and a suppressor that rounds them is one that cannot be
        // A/B'd against itself.
        Assert.Equal(original, block);
    }

    [Fact(
        Skip = SkipReason,
        SkipType = typeof(RnnoiseSuppressor),
        SkipUnless = nameof(RnnoiseSuppressor.IsAvailable))]
    [Trait("Category", TestCategories.Unit)]
    public void ItStillWorksAfterAReset()
    {
        using RnnoiseSuppressor suppressor = new();

        float[] block = new float[RnnoiseSuppressor.FrameSamples];
        Random noise = new(20260830);

        for (int frame = 0; frame < SettleFrames; frame++)
        {
            Fill(block, noise);
            suppressor.Process(block, 1f);
        }

        suppressor.Reset();

        for (int frame = 0; frame < SettleFrames; frame++)
        {
            Fill(block, noise);
            suppressor.Process(block, 1f);
        }

        Fill(block, noise);

        float before = Rms(block);
        suppressor.Process(block, 1f);

        Assert.True(Rms(block) < before * 0.5f, "Reset left the denoiser unable to denoise.");
    }

    [Fact(
        Skip = SkipReason,
        SkipType = typeof(RnnoiseSuppressor),
        SkipUnless = nameof(RnnoiseSuppressor.IsAvailable))]
    [Trait("Category", TestCategories.Unit)]
    public void ProcessDoesNotAllocate()
    {
        using RnnoiseSuppressor suppressor = new();

        float[] block = new float[RnnoiseSuppressor.FrameSamples];
        Fill(block, new Random(20260830));

        // The closure-free overload: the capturing one measures its own closure and would pass or
        // fail for a reason that has nothing to do with the suppressor.
        AllocationAssert.None(
            (suppressor, block),
            static state => state.suppressor.Process(state.block, 1f));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AvailabilityIsAnswerableWithoutTheLibrary()
    {
        // Not gated, because this is the one that has to hold when rnnoise.dll is absent: the probe
        // reports, it does not throw. An engine that crashed on a machine without the optional
        // library would make the fallback pointless.
        bool available = RnnoiseSuppressor.IsAvailable;

        output.WriteLine(available ? "rnnoise.dll is present." : "rnnoise.dll is absent.");
    }

    static void Fill(float[] block, Random noise)
    {
        for (int index = 0; index < block.Length; index++)
        {
            block[index] = ((float)noise.NextDouble() * 2f) - 1f;
        }
    }

    static float Rms(ReadOnlySpan<float> block)
    {
        double sum = 0.0;

        for (int index = 0; index < block.Length; index++)
        {
            sum += block[index] * (double)block[index];
        }

        return (float)Math.Sqrt(sum / block.Length);
    }

    static double Ratio(float before, float after) =>
        20.0 * Math.Log10(Math.Max(before, float.Epsilon) / Math.Max(after, float.Epsilon));
}
