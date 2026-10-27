using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Recording;
using Vam.Engine.Recording.Extensions;
using Vam.Engine.Recording.Flac;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Recording;

/// <summary>#98. FLAC is the same recording as the WAV, smaller.</summary>
public class FlacRecordingTests : IDisposable
{
    const int SampleRate = 48000;
    const int BlockFrames = 120;

    readonly string directory = Path.Combine(Path.GetTempPath(), "vam-flac-" + Guid.NewGuid().ToString("n"));

    public FlacRecordingTests() => Directory.CreateDirectory(directory);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFlacRecordingDecodesToTheSamplesOfTheWavOfTheSameInput()
    {
        // Three whole blocks and a short one, so the last frame takes the other header form.
        int frames = (3 * FlacFrameEncoder.BlockFrames) + 1234;
        float[] input = Signal(frames, 2);

        string wav = Record(input, 2, RecordingContainer.Wave);
        string flac = Record(input, 2, RecordingContainer.Flac);

        (int[] wavSamples, byte[] wavData) = ReadWave(wav);
        (FlacStreamInfo info, int[] flacSamples) = FlacTestDecoder.Decode(flac);

        Assert.Equal(wavSamples, flacSamples);
        Assert.Equal(frames, info.TotalSamples);
        Assert.Equal(SampleRate, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(24, info.BitsPerSample);

        // The checksum a FLAC player verifies is over the same bytes the WAV holds.
        Assert.Equal(MD5.HashData(wavData), info.Md5);

        // Lossless and still smaller: the sine and the silence compress, the noise does not.
        Assert.True(new FileInfo(flac).Length < new FileInfo(wav).Length);
    }

    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 4096)]
    [InlineData(2, 17)]
    [InlineData(6, 9000)]
    [InlineData(8, 4097)]
    public void EveryLengthAndWidthRoundTrips(int channels, int frames)
    {
        float[] input = Signal(frames, channels);
        string path = Path.Combine(directory, $"round-{channels}-{frames}.flac");

        using (FlacWriter writer = new(path, SampleRate, channels, BlockFrames))
        {
            for (int at = 0; at < frames; at += BlockFrames)
            {
                int take = Math.Min(BlockFrames, frames - at);

                writer.Write(input.AsSpan(at * channels, take * channels));
            }

            writer.Finish();

            Assert.Equal(frames, writer.FrameCount);
        }

        (FlacStreamInfo info, int[] decoded) = FlacTestDecoder.Decode(path);
        int[] expected = Array.ConvertAll(input, sample => sample.ToPcm24());

        Assert.Equal(expected, decoded);
        Assert.Equal(frames, info.TotalSamples);
        Assert.Equal(channels, info.Channels);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFlacSessionWritesFlacFilesAndIsListedAfterwards()
    {
        string root = Path.Combine(directory, "sessions");
        string folder = Path.Combine(root, "2026-10-27_10-00-00");
        RecordingTrack track;

        using (RecordingSession session = new(folder, new DiskGuard(NullLogger<DiskGuard>.Instance), NullLogger<RecordingSession>.Instance))
        {
            track = session.AddTrack(
                "Mayor",
                new RecordingFormat { SampleRate = SampleRate, ChannelCount = 1, BlockFrames = BlockFrames, Container = RecordingContainer.Flac },
                new RecordingSource(RecordingSourceKind.Channel, 0));

            Assert.True(track.Capture(Signal(BlockFrames, 1), BlockFrames));
            track.Drain();
            session.Stop();
        }

        Assert.EndsWith(".flac", track.Path, StringComparison.Ordinal);

        RecordedSession listed = Assert.Single(new RecordingCatalogue(NullLogger<RecordingCatalogue>.Instance).Read([root]).Sessions);

        Assert.Equal(1, listed.Tracks);
    }

    string Record(float[] input, int channels, RecordingContainer container)
    {
        RecordingFormat format = new() { SampleRate = SampleRate, ChannelCount = channels, BlockFrames = BlockFrames, Container = container };
        string path = Path.Combine(directory, "track" + format.FileExtension);

        using RecordingTrack track = new("track", path, format, new RecordingSource(RecordingSourceKind.Bus, 0));

        for (int at = 0; at < input.Length; at += BlockFrames * channels)
        {
            int take = Math.Min(BlockFrames * channels, input.Length - at);

            Assert.True(track.Capture(input.AsSpan(at, take), take / channels));
            track.Drain();
        }

        track.Finish();

        return path;
    }

    // A sine, silence, full-scale noise and a clipped ramp in turn, so the constant, fixed and
    // verbatim subframes all occur, and each channel differs from the others.
    static float[] Signal(int frames, int channels)
    {
        Random random = new(98);
        float[] samples = new float[frames * channels];

        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                samples[(frame * channels) + channel] = ((frame / 3000) % 4) switch
                {
                    0 => 0.5f * MathF.Sin(frame * 0.01f * (channel + 1)),
                    1 => channel == 0 ? 0f : 0.25f,
                    2 => (random.NextSingle() * 2f) - 1f,
                    _ => (frame % 3000 / 1000f) - 1.5f
                };
            }
        }

        return samples;
    }

    static (int[] Samples, byte[] Data) ReadWave(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int at = 12;

        while (System.Text.Encoding.ASCII.GetString(bytes, at, 4) != "data")
        {
            at += 8 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
        byte[] data = bytes.AsSpan(at + 8, length).ToArray();
        int[] samples = new int[length / 3];

        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = (data[index * 3] | (data[(index * 3) + 1] << 8) | (data[(index * 3) + 2] << 16)) << 8 >> 8;
        }

        return (samples, data);
    }
}
