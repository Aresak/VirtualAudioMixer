using Vam.Engine.Devices.Clock;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// VAM-015. Measured rather than eyeballed, because a resampler that looks fine on a waveform can
/// still be audibly wrong on speech.
/// </summary>
/// <remarks>
/// <b>A listening check against real speech is still owed and cannot be done here</b> - no
/// recording of a real session exists yet. These tests bound the distortion; they do not settle
/// whether it sounds right.
/// </remarks>
public class DriftResamplerTests(ITestOutputHelper output)
{
    const int SampleRate = 48000;
    const int BlockFrames = 480;

    /// <summary>
    /// Signal to distortion floor demanded of the resampler. Far below the noise floor of any
    /// conference microphone, so the resampler is not what limits quality.
    /// </summary>
    const double MinimumSignalToDistortionDb = 80.0;

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.0 - 0.0002)]
    [InlineData(1.0 + 0.0002)]
    [InlineData(1.0 - 0.00005)]
    [InlineData(1.0 + 0.00005)]
    [Trait("Category", TestCategories.Unit)]
    public void ASineSurvivesTheWholeRatioRangeCleanly(double ratio)
    {
        // Every tone is measured before anything is asserted, so a failure reports the whole
        // picture rather than stopping at the first one and hiding the shape of the problem.
        double[] tones = [1000.0, 4000.0, 8000.0];
        double[] measured = new double[tones.Length];

        for (int index = 0; index < tones.Length; index++)
        {
            measured[index] = MeasureSignalToDistortionDb(ratio, tones[index]);
            output.WriteLine($"ratio {ratio:F6}, {tones[index]:F0} Hz: {measured[index]:F1} dB");
        }

        for (int index = 0; index < tones.Length; index++)
        {
            Assert.True(
                measured[index] > MinimumSignalToDistortionDb,
                $"{tones[index]:F0} Hz at ratio {ratio:F6} measured {measured[index]:F1} dB, "
                + $"below the {MinimumSignalToDistortionDb:F0} dB floor.");
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SweepingTheRatioWhileRunningProducesNoDiscontinuity()
    {
        // The servo nudges the ratio continuously. If the fractional read position did not carry
        // across calls, every change would step the phase and put a click in the output.
        DriftResampler resampler = new(1, BlockFrames);

        const double toneHz = 1000.0;
        const int blocks = 200;

        float[] input = new float[BlockFrames];
        float[] outputBlock = new float[BlockFrames * 2];
        List<float> collected = new(blocks * BlockFrames);

        double phase = 0.0;
        double increment = 2.0 * Math.PI * toneHz / SampleRate;

        for (int block = 0; block < blocks; block++)
        {
            for (int frame = 0; frame < BlockFrames; frame++)
            {
                input[frame] = (float)Math.Sin(phase);
                phase += increment;
            }

            // Swept right across the permitted range during the run, not set once beforehand.
            double progress = (double)block / (blocks - 1);
            resampler.Ratio = 1.0 - 0.0002 + (progress * 0.0004);

            resampler.Process(input, outputBlock, out _, out int produced);
            collected.AddRange(outputBlock.AsSpan(0, produced).ToArray());
        }

        // A sine at this frequency moves by at most this much between adjacent samples. A phase
        // step at a block boundary would produce a jump far larger, so this catches one without
        // needing to know where the boundaries fell.
        double maximumStep = Math.Abs(Math.Sin(increment)) * 1.5;
        double worstStep = 0.0;
        int worstIndex = -1;

        // The first frames are the filter filling up, which is not a discontinuity.
        for (int index = DriftResampler.Taps + 1; index < collected.Count; index++)
        {
            double step = Math.Abs(collected[index] - collected[index - 1]);

            if (step > worstStep)
            {
                worstStep = step;
                worstIndex = index;
            }
        }

        output.WriteLine($"worst adjacent step {worstStep:F6} at {worstIndex}, allowed {maximumStep:F6}");

        Assert.True(
            worstStep < maximumStep,
            $"Step of {worstStep:F6} at sample {worstIndex} exceeds the {maximumStep:F6} a continuous sine can make.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ProcessingAllocatesNothing()
    {
        DriftResampler resampler = new(2, BlockFrames);
        resampler.Ratio = 1.000067;

        float[] input = new float[BlockFrames * 2];
        float[] outputBlock = new float[BlockFrames * 2 * 2];

        for (int index = 0; index < input.Length; index++)
        {
            input[index] = MathF.Sin(index * 0.01f);
        }

        AllocationAssert.None((resampler, input, outputBlock), static state =>
            state.resampler.Process(state.input, state.outputBlock, out _, out _));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AtUnityRatioTheOutputTracksTheInputFrameForFrame()
    {
        DriftResampler resampler = new(1, BlockFrames);
        float[] input = new float[BlockFrames];
        float[] outputBlock = new float[BlockFrames];

        resampler.Process(input, outputBlock, out int consumed, out int produced);

        Assert.Equal(BlockFrames, consumed);
        Assert.Equal(BlockFrames, produced);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFastDeviceHasMoreInputConsumedThanOutputProduced()
    {
        // 200 ppm fast over a long run has to consume measurably more than it produces, or it is
        // not correcting anything.
        DriftResampler resampler = new(1, BlockFrames);
        resampler.Ratio = 1.0 + 0.0002;

        float[] input = new float[BlockFrames];
        float[] outputBlock = new float[BlockFrames];

        long totalConsumed = 0;
        long totalProduced = 0;

        for (int block = 0; block < 500; block++)
        {
            resampler.Process(input, outputBlock, out int consumed, out int produced);
            totalConsumed += consumed;
            totalProduced += produced;
        }

        Assert.True(
            totalConsumed > totalProduced,
            $"Consumed {totalConsumed} and produced {totalProduced}; a fast device must consume more.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARatioBeyondPlausibleDriftIsRefused()
    {
        DriftResampler resampler = new(1, BlockFrames);

        // Not drift. Accepting it silently would let a broken estimate destroy the audio.
        Assert.Throws<ArgumentOutOfRangeException>(() => resampler.Ratio = 1.01);
        Assert.Throws<ArgumentOutOfRangeException>(() => resampler.Ratio = 0.99);
    }

    static double MeasureSignalToDistortionDb(double ratio, double toneHz)
    {
        DriftResampler resampler = new(1, BlockFrames);
        resampler.Ratio = ratio;

        const int blocks = 120;
        float[] input = new float[BlockFrames];
        float[] outputBlock = new float[BlockFrames * 2];
        List<double> collected = new(blocks * BlockFrames);

        double phase = 0.0;
        double increment = 2.0 * Math.PI * toneHz / SampleRate;

        for (int block = 0; block < blocks; block++)
        {
            for (int frame = 0; frame < BlockFrames; frame++)
            {
                input[frame] = (float)Math.Sin(phase);
                phase += increment;
            }

            resampler.Process(input, outputBlock, out _, out int produced);

            for (int frame = 0; frame < produced; frame++)
            {
                collected.Add(outputBlock[frame]);
            }
        }

        // Skip the filter warm-up: those frames are genuinely incomplete, not distorted.
        int skip = DriftResampler.Taps * 4;
        int length = collected.Count - skip;

        // Output frequency is the input's, scaled by the ratio: output frame n reads input
        // position n * ratio.
        double outputIncrement = increment * ratio;

        // Fit amplitude and phase of the fundamental by least squares, subtract it, and whatever is
        // left is distortion plus noise. Avoids the spectral leakage a windowed transform would
        // introduce, which at these levels would swamp what is being measured.
        //
        // Solved properly through the normal equations rather than assuming cosine and sine are
        // orthogonal over the window. They are only orthogonal over a whole number of cycles, and
        // the leakage from assuming it puts a floor at about -77 dB - which is above the distortion
        // being measured, so the test would have been reporting its own error.
        double sumCosCos = 0.0;
        double sumSinSin = 0.0;
        double sumCosSin = 0.0;
        double sumYCos = 0.0;
        double sumYSin = 0.0;

        for (int index = 0; index < length; index++)
        {
            double angle = outputIncrement * index;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            double value = collected[skip + index];

            sumCosCos += cosine * cosine;
            sumSinSin += sine * sine;
            sumCosSin += cosine * sine;
            sumYCos += value * cosine;
            sumYSin += value * sine;
        }

        double determinant = (sumCosCos * sumSinSin) - (sumCosSin * sumCosSin);
        double amplitudeCos = ((sumYCos * sumSinSin) - (sumYSin * sumCosSin)) / determinant;
        double amplitudeSin = ((sumYSin * sumCosCos) - (sumYCos * sumCosSin)) / determinant;

        double signalPower = 0.0;
        double residualPower = 0.0;

        for (int index = 0; index < length; index++)
        {
            double angle = outputIncrement * index;
            double fundamental = (amplitudeCos * Math.Cos(angle)) + (amplitudeSin * Math.Sin(angle));
            double actual = collected[skip + index];
            double residual = actual - fundamental;

            signalPower += fundamental * fundamental;
            residualPower += residual * residual;
        }

        if (residualPower <= 0.0)
        {
            return double.PositiveInfinity;
        }

        return 10.0 * Math.Log10(signalPower / residualPower);
    }
}
