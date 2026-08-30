using System.Runtime.InteropServices;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// The one part of the WASAPI path that can be checked without a device: the conversion from
/// whatever the driver hands back into the interleaved floats the engine works in.
/// </summary>
/// <remarks>
/// Worth testing on its own precisely because it is the only piece where a mistake is silent. A
/// wrong scale factor or a missed sub-format does not throw, it just makes everything quiet or
/// clipped, and by then it is being blamed on a microphone.
/// </remarks>
public class WasapiSampleReaderTests
{
    const int SampleRate = 48000;
    const int Frames = 64;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void FloatSamplesArriveUnchanged()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
        WasapiSampleReader reader = new(format, Frames);

        Assert.True(reader.IsFloatFormat);

        float[] source = new float[Frames * 2];

        for (int index = 0; index < source.Length; index++)
        {
            source[index] = (index * 0.001f) - 0.5f;
        }

        RunAgainstPinnedBuffer(source, buffer =>
        {
            ReadOnlySpan<float> read = reader.Read(buffer, Frames, isSilent: false);

            Assert.Equal(source.Length, read.Length);

            for (int index = 0; index < source.Length; index++)
            {
                Assert.Equal(source[index], read[index]);
            }
        });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SixteenBitSamplesLandOnTheExpectedScale()
    {
        WaveFormat format = new(SampleRate, 16, 1);
        WasapiSampleReader reader = new(format, Frames);

        Assert.False(reader.IsFloatFormat);

        // The endpoints are what a scale error shows up in: full negative must reach exactly -1,
        // and full positive must land just under +1 rather than wrapping past it.
        short[] source = [short.MinValue, short.MaxValue, 0, 16384];

        RunAgainstPinnedBuffer(source, buffer =>
        {
            ReadOnlySpan<float> read = reader.Read(buffer, source.Length, isSilent: false);

            Assert.Equal(-1.0f, read[0]);
            Assert.Equal(0.99997f, read[1], 0.0001f);
            Assert.Equal(0.0f, read[2]);
            Assert.Equal(0.5f, read[3]);
        });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASilentPacketIsProducedRatherThanCopied()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        WasapiSampleReader reader = new(format, Frames);

        float[] rubbish = new float[Frames];
        Array.Fill(rubbish, 42.0f);

        // WASAPI is entitled to hand back a buffer whose contents are undefined when it flags
        // silence. Copying it would put whatever was left in that memory into the mix.
        RunAgainstPinnedBuffer(rubbish, buffer =>
        {
            ReadOnlySpan<float> read = reader.Read(buffer, Frames, isSilent: true);

            foreach (float sample in read)
            {
                Assert.Equal(0.0f, sample);
            }
        });
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ReadingAllocatesNothing()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
        WasapiSampleReader reader = new(format, Frames);
        float[] source = new float[Frames * 2];

        GCHandle pin = GCHandle.Alloc(source, GCHandleType.Pinned);

        try
        {
            (WasapiSampleReader Reader, nint Buffer) state = (reader, pin.AddrOfPinnedObject());

            AllocationAssert.None(state, static work => work.Reader.Read(work.Buffer, Frames, isSilent: false));
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFormatWithNoConversionIsRefusedWhenTheStreamOpens()
    {
        // 24-bit is common on interfaces VAM has not met yet. Refusing here is the point: the
        // alternative is discovering it inside a callback, which is the one place that must not
        // throw.
        WaveFormat format = new(SampleRate, 24, 2);

        UnsupportedAudioFormatException error =
            Assert.Throws<UnsupportedAudioFormatException>(() => new WasapiSampleReader(format, Frames));

        Assert.Contains("24-bit", error.Message, StringComparison.Ordinal);
    }

    static void RunAgainstPinnedBuffer<T>(T[] source, Action<nint> body)
        where T : struct
    {
        GCHandle pin = GCHandle.Alloc(source, GCHandleType.Pinned);

        try
        {
            body(pin.AddrOfPinnedObject());
        }
        finally
        {
            pin.Free();
        }
    }
}
