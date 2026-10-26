using Vam.TestKit.Dsp;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Dsp;

/// <summary>
/// Calibrates the instrument L1 depends on. An analyser nothing has checked would let the loopback
/// test pass or fail for reasons of its own.
/// </summary>
/// <remarks>
/// The last test here is the point of the others: it feeds the analyser exactly the mistake the
/// stereo-as-mono defect made, and shows that the reading moves an octave. An instrument that
/// cannot see the defect cannot be the thing that catches it.
/// </remarks>
public class ToneAnalyserTests
{
    const int SampleRate = 48000;
    const int Frames = 48000;
    const double ToneHz = 1000.0;

    /// <summary>Within a tenth of a percent, so the 2 % L1 allows is spent on the devices.</summary>
    const double ToleranceHz = 1.0;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AMonoToneReadsAtItsOwnFrequency()
    {
        ToneAnalyser analyser = new(SampleRate, 1);

        ToneMeasurement measurement = analyser.Measure(Tone(ToneHz, 1));

        Assert.Equal(ToneHz, measurement.FrequencyHz, ToleranceHz);
        Assert.Equal(0.5f, measurement.Peak, 0.01f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AnInterleavedStereoToneReadsAtItsOwnFrequencyOnBothChannels()
    {
        float[] audio = Tone(ToneHz, 2);
        ToneAnalyser analyser = new(SampleRate, 2);

        Assert.Equal(ToneHz, analyser.Measure(audio, 0).FrequencyHz, ToleranceHz);
        Assert.Equal(ToneHz, analyser.Measure(audio, 1).FrequencyHz, ToleranceHz);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void EachChannelIsReadSeparately()
    {
        float[] audio = new float[Frames * 2];

        Fill(audio, ToneHz, 2, 0);
        Fill(audio, ToneHz * 1.5, 2, 1);

        ToneAnalyser analyser = new(SampleRate, 2);

        Assert.Equal(ToneHz, analyser.Measure(audio, 0).FrequencyHz, ToleranceHz);
        Assert.Equal(ToneHz * 1.5, analyser.Measure(audio, 1).FrequencyHz, ToleranceHz);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheToneIsFoundPastTheSilenceADevicePrimesWith()
    {
        float[] audio = new float[Frames];

        // Nothing for the first half second, which is what a device that has just opened delivers.
        Fill(audio.AsSpan(SampleRate / 2), ToneHz, 1, 0);

        ToneMeasurement measurement = new ToneAnalyser(SampleRate, 1).Measure(audio);

        Assert.Equal(ToneHz, measurement.FrequencyHz, ToleranceHz);
        Assert.True(
            measurement.WindowStartFrame >= SampleRate / 2,
            $"The loudest window was taken at frame {measurement.WindowStartFrame}, inside the silence.");
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AStereoBufferReadAsMonoComesBackAnOctaveDown()
    {
        // The stereo-as-mono defect, exactly: two interleaved channels arrived and the path read
        // them as one, so every frame consumed twice the samples and the pitch halved. Audible
        // immediately, invisible to 292 tests, and this is the reading that would have shown it.
        float[] stereo = Tone(ToneHz, 2);

        Assert.Equal(ToneHz, new ToneAnalyser(SampleRate, 2).Measure(stereo).FrequencyHz, ToleranceHz);
        Assert.Equal(ToneHz / 2.0, new ToneAnalyser(SampleRate, 1).Measure(stereo).FrequencyHz, ToleranceHz);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(107.0)]
    [InlineData(3100.0)]
    public void OneLoudTransientDoesNotBecomeTheMeasurement(double burstHz)
    {
        // Taken from a real acoustic run: a knock on the desk read as 107 Hz at twelve times the
        // level of the tone. One burst below the tone and one above, so only the middle of the three
        // readings passes both - not the loudest window, not the lowest reading, not the highest.
        float[] audio = Tone(ToneHz, 1);

        AddBurst(audio, burstHz, 20000);

        Assert.Equal(ToneHz, new ToneAnalyser(SampleRate, 1).Measure(audio).FrequencyHz, ToleranceHz);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(1000.0)]
    [InlineData(1250.0)]
    public void AToneBetweenBinsIsReadToAQuarterOfAHertz(double frequencyHz)
    {
        // Both sit near a third of a bin off centre. The parabola reads them to a tenth of a hertz
        // through a Hann window and is a whole hertz out through a rectangular one.
        Assert.Equal(frequencyHz, new ToneAnalyser(SampleRate, 1).Measure(Tone(frequencyHz, 1)).FrequencyHz, 0.25);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(2000)]
    [InlineData(6000)]
    [InlineData(18000)]
    [InlineData(34000)]
    public void AtTheShortestLengthAcceptedTheThreeWindowsStillCannotOverlap(int burstStart)
    {
        float[] audio = Tone(ToneHz, 1, 5 * 8192);

        AddBurst(audio, 107.0, burstStart);

        Assert.Equal(ToneHz, new ToneAnalyser(SampleRate, 1).Measure(audio).FrequencyHz, ToleranceHz);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TooLittleRoomForThreeSeparateWindowsIsRefused()
    {
        // Three windows' worth exactly. Loudest-first placement put the burst's window in the
        // middle, found no room left for the third, and quietly measured an overlapping one.
        float[] audio = Tone(ToneHz, 1, 3 * 8192);

        AddBurst(audio, 107.0, 2000);

        Assert.Throws<ArgumentException>(() => new ToneAnalyser(SampleRate, 1).Measure(audio));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TooLittleAudioToMeasureSaysSoRatherThanGuessing()
    {
        ToneAnalyser analyser = new(SampleRate, 1);

        Assert.Throws<ArgumentException>(() => analyser.Measure(new float[100]));
    }

    static float[] Tone(double frequencyHz, int channelCount, int frames = Frames)
    {
        float[] audio = new float[frames * channelCount];

        for (int channel = 0; channel < channelCount; channel++)
        {
            Fill(audio, frequencyHz, channelCount, channel);
        }

        return audio;
    }

    static void AddBurst(float[] audio, double frequencyHz, int start)
    {
        double increment = 2.0 * Math.PI * frequencyHz / SampleRate;

        for (int frame = 0; frame < 3000; frame++)
        {
            audio[start + frame] += (float)(Math.Sin(increment * frame) * 6.0);
        }
    }
    static void Fill(Span<float> audio, double frequencyHz, int channelCount, int channel)
    {
        double increment = 2.0 * Math.PI * frequencyHz / SampleRate;
        int frames = audio.Length / channelCount;

        for (int frame = 0; frame < frames; frame++)
        {
            audio[(frame * channelCount) + channel] = (float)(Math.Sin(increment * frame) * 0.5);
        }
    }
}
