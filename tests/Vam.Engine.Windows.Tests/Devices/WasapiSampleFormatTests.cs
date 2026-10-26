using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// The rule exclusive mode is gated on: a device's own format is taken exclusively only when the
/// sample reader can convert it, or the open succeeds and the stream is refused on every retry.
/// </summary>
public class WasapiSampleFormatTests
{
    const int SampleRate = 48000;

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(16, true)]
    [InlineData(24, false)]
    [InlineData(32, false)]
    public void IntegerPcmIsConvertibleOnlyAtSixteenBits(int bits, bool expected)
    {
        Assert.Equal(expected, WasapiSampleFormat.IsConvertible(new WaveFormat(SampleRate, bits, 2)));
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(16, true)]
    [InlineData(24, false)]
    [InlineData(32, true)]
    public void AnExtensibleFormatIsJudgedByItsSubFormat(int bits, bool expected)
    {
        // NAudio's extensible format is float at 32 bits and integer PCM below.
        Assert.Equal(expected, WasapiSampleFormat.IsConvertible(new WaveFormatExtensible(SampleRate, bits, 2)));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void PlainFloatIsConvertible()
    {
        Assert.True(WasapiSampleFormat.IsConvertible(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheReaderRefusesExactlyWhatTheRuleRefuses()
    {
        WaveFormat format = new WaveFormatExtensible(SampleRate, 24, 2);

        Assert.False(WasapiSampleFormat.IsConvertible(format));
        Assert.Throws<UnsupportedAudioFormatException>(
            () => new WasapiSampleReader(format, 64));
    }
}
