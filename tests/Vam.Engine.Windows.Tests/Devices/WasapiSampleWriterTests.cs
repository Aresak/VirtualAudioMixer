using System.Runtime.InteropServices;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.Wasapi;
using Vam.TestKit.Allocations;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Windows.Tests.Devices;

/// <summary>
/// The render side's conversion, checked without a speaker.
/// </summary>
public class WasapiSampleWriterTests
{
    const int SampleRate = 48000;
    const int Frames = 32;
    const int Channels = 2;

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFloatDeviceIsWrittenIntoDirectly()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        WasapiSampleWriter writer = new(format, Frames);

        Assert.True(writer.IsFloatFormat);

        float[] deviceBuffer = new float[Frames * Channels];

        RunAgainstPinnedBuffer(deviceBuffer, buffer =>
        {
            Span<float> destination = writer.Prepare(buffer, Frames);

            destination.Fill(0.25f);
            writer.Commit(buffer, Frames, Frames);
        });

        // The point of the float path: what the graph wrote landed in the device's own buffer with
        // no copy in between, so this array is the proof rather than a comparison against a scratch.
        Assert.All(deviceBuffer, sample => Assert.Equal(0.25f, sample));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AShortFillIsSilencedRatherThanLeftHoldingOldAudio()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        WasapiSampleWriter writer = new(format, Frames);

        float[] deviceBuffer = new float[Frames * Channels];

        // Whatever the device played last time is still sitting here. Leaving it would replay a
        // fragment of old audio, which is a stutter rather than the gap it stands in for.
        Array.Fill(deviceBuffer, 0.9f);

        const int Filled = 8;

        RunAgainstPinnedBuffer(deviceBuffer, buffer =>
        {
            Span<float> destination = writer.Prepare(buffer, Frames);

            destination[..(Filled * Channels)].Fill(0.5f);
            writer.Commit(buffer, Frames, Filled);
        });

        for (int index = 0; index < deviceBuffer.Length; index++)
        {
            Assert.Equal(index < Filled * Channels ? 0.5f : 0.0f, deviceBuffer[index]);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SixteenBitOutputIsScaledAndClamped()
    {
        WaveFormat format = new(SampleRate, 16, 1);
        WasapiSampleWriter writer = new(format, Frames);

        Assert.False(writer.IsFloatFormat);

        short[] deviceBuffer = new short[4];

        RunAgainstPinnedBuffer(deviceBuffer, buffer =>
        {
            Span<float> destination = writer.Prepare(buffer, 4);

            // The last two are out of range on purpose. A graph that overshoots must clip rather
            // than wrap, because a wrap turns a loud moment into a burst of noise.
            destination[0] = 0.0f;
            destination[1] = 0.5f;
            destination[2] = 2.0f;
            destination[3] = -2.0f;

            writer.Commit(buffer, 4, 4);
        });

        Assert.Equal(0, deviceBuffer[0]);
        Assert.Equal(16383, deviceBuffer[1]);
        Assert.Equal(short.MaxValue, deviceBuffer[2]);
        Assert.Equal(-short.MaxValue, deviceBuffer[3]);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WritingAllocatesNothing()
    {
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        WasapiSampleWriter writer = new(format, Frames);
        float[] deviceBuffer = new float[Frames * Channels];

        GCHandle pin = GCHandle.Alloc(deviceBuffer, GCHandleType.Pinned);

        try
        {
            (WasapiSampleWriter Writer, nint Buffer) state = (writer, pin.AddrOfPinnedObject());

            AllocationAssert.None(state, static work =>
            {
                Span<float> destination = work.Writer.Prepare(work.Buffer, Frames);

                destination.Fill(0.1f);
                work.Writer.Commit(work.Buffer, Frames, Frames);
            });
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
        WaveFormat format = new(SampleRate, 24, 2);

        Assert.Throws<UnsupportedAudioFormatException>(() => new WasapiSampleWriter(format, Frames));
    }

    static void RunAgainstPinnedBuffer<T>(T[] buffer, Action<nint> body)
        where T : struct
    {
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

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
