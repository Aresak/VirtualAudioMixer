using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;
using Vam.Engine.Recording;
using Vam.TestKit.Allocations;
using Vam.TestKit.Graph;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Recording;

/// <summary>
/// EPIC-07. The record of the meeting, and the guards that stop it failing quietly.
/// </summary>
/// <remarks>
/// Bit-accuracy is the acceptance test, not "it sounds right". Anything less does not prove the
/// thing this epic exists for — that a session which went wrong can still be rebuilt afterwards.
/// </remarks>
public class RecordingTests : IDisposable
{
    const int SampleRate = 48000;
    const int BlockFrames = 120;

    static readonly AudioDeviceId Microphone = new("null:capture:mayor");

    readonly string directory = Path.Combine(Path.GetTempPath(), "vam-recording-" + Guid.NewGuid().ToString("n"));

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
    public void WhatWasCapturedIsWhatWasWritten()
    {
        RecordingFormat format = new() { SampleRate = SampleRate, ChannelCount = 1, BlockFrames = BlockFrames };

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "track.wav");
        float[] written = new float[BlockFrames * 8];

        // A ramp across the whole range, so a scale error, a sign error or a truncation all show.
        for (int index = 0; index < written.Length; index++)
        {
            written[index] = (index / (float)written.Length * 2f) - 1f;
        }

        using (RecordingTrack track = new("track", path, format))
        {
            for (int block = 0; block < 8; block++)
            {
                Assert.True(track.Capture(written.AsSpan(block * BlockFrames, BlockFrames), BlockFrames));
                track.Drain();
            }

            track.Finish();

            Assert.Equal(0, track.DroppedFrames);
        }

        float[] read = ReadSamples(path);

        Assert.Equal(written.Length, read.Length);

        for (int index = 0; index < written.Length; index++)
        {
            // Twenty-four bit resolution is about one part in eight million. Anything larger than
            // that is not a rounding difference, it is a bug.
            Assert.Equal(written[index], read[index], 1f / 4_000_000f);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFullRingDropsAndCountsRatherThanWaiting()
    {
        RecordingFormat format = new() { SampleRate = 100, ChannelCount = 1, BlockFrames = BlockFrames };

        Directory.CreateDirectory(directory);

        using RecordingTrack track = new("tiny", Path.Combine(directory, "tiny.wav"), format);

        float[] block = new float[BlockFrames];
        int refused = 0;

        // The ring holds two seconds at a hundred hertz, so it fills in a couple of blocks. Nothing
        // drains it, which is a disk that has stopped answering.
        for (int index = 0; index < 20; index++)
        {
            if (!track.Capture(block, BlockFrames))
            {
                refused++;
            }
        }

        // A failing disk must not be able to stop a live broadcast. It costs a counted gap.
        Assert.True(refused > 0);
        Assert.Equal(refused * BlockFrames, track.DroppedFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFileThatFitsStaysPlainRiff()
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "small.wav");

        using (WaveWriter writer = new(path, SampleRate, 1, BlockFrames))
        {
            writer.Write(new float[BlockFrames]);
            writer.Finish();

            Assert.False(writer.NeedsRf64);
        }

        Assert.Equal("RIFF", ReadFourCharacterCode(path, 0));

        // The placeholder is there whether or not it was needed. Twenty-eight bytes in a file that
        // never grows, against having to rewrite the whole file if it does.
        Assert.Equal("JUNK", ReadFourCharacterCode(path, 12));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheDiskGuardRefusesWithTheNumbersInTheMessage()
    {
        DiskGuard guard = new(NullLogger<DiskGuard>.Instance);

        Directory.CreateDirectory(directory);

        // Far more than any machine has.
        DiskVerdict verdict = guard.CheckBeforeStart(directory, long.MaxValue / 2);

        Assert.False(verdict.CanStart);

        // "Not enough space" leaves an operator at five to seven with a decision they cannot make.
        Assert.Contains("free", verdict.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GB", verdict.Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASessionWithRoomStartsAndWritesEveryStrip()
    {
        using RecordingSession session = new(
            directory,
            new DiskGuard(NullLogger<DiskGuard>.Instance),
            NullLogger<RecordingSession>.Instance
        );

        RecordingFormat format = new() { SampleRate = SampleRate, ChannelCount = 1, BlockFrames = BlockFrames };

        session.AddTrack("Mayor 180 degrees", format);

        DiskVerdict verdict = session.Start(TimeSpan.FromSeconds(10));

        Assert.True(verdict.CanStart);
        Assert.True(session.IsRecording);

        ConsoleFixture console = Build(session);

        console.Feed(0, 0.4f);

        for (int block = 0; block < 50; block++)
        {
            console.Render();
        }

        session.Stop();

        RecordingTrack track = Assert.Single(session.Tracks);

        Assert.True(track.FramesWritten > 0, "Nothing reached the file.");
        Assert.Equal(0, track.DroppedFrames);

        float[] read = ReadSamples(track.Path);

        // Tapped after the trim and before everything else, so what is on disk is what the
        // microphone sent rather than what the console made of it.
        Assert.Contains(read, sample => Math.Abs(sample - 0.4f) < 0.001f);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ClosingASessionTwiceIsNotAnError()
    {
        RecordingSession session = new(
            directory,
            new DiskGuard(NullLogger<DiskGuard>.Instance),
            NullLogger<RecordingSession>.Instance
        );

        session.AddTrack(
            "Mayor 180 degrees",
            new RecordingFormat { SampleRate = SampleRate, ChannelCount = 1, BlockFrames = BlockFrames });

        Assert.True(session.Start(TimeSpan.FromSeconds(10)).CanStart);

        ConsoleFixture console = Build(session);

        console.Feed(0, 0.4f);

        for (int block = 0; block < 20; block++)
        {
            console.Render();
        }

        session.Stop();

        // Held onto, because Dispose empties the session's list. The track outlives it and its file
        // is the thing worth checking.
        RecordingTrack track = session.Tracks[0];
        long written = track.FramesWritten;

        // Closing runs on every path out, including a fault, which means it runs more than once: an
        // operator stopping a recording and then the engine shutting down is the ordinary case. A
        // second call that threw turned a tidy exit into an unhandled exception on the way out of
        // the process, with the meeting already on disk.
        session.Stop();
        session.Dispose();
        session.Dispose();

        Assert.Equal(written, track.FramesWritten);

        // And the file is still a file, with its sizes patched exactly once.
        float[] read = ReadSamples(track.Path);

        Assert.NotEmpty(read);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TappingForTheRecordingAllocatesNothing()
    {
        using RecordingSession session = new(
            directory,
            new DiskGuard(NullLogger<DiskGuard>.Instance),
            NullLogger<RecordingSession>.Instance
        );

        session.AddTrack("Mayor 180 degrees", new RecordingFormat
        {
            SampleRate = SampleRate,
            ChannelCount = 1,
            BlockFrames = BlockFrames
        });

        ConsoleFixture console = Build(session);

        console.Feed(0, 0.3f);
        console.RenderUntilSettled();

        AllocationAssert.None(console, static fixture => fixture.Render());
    }

    static ConsoleFixture Build(RecordingSession session)
    {
        GraphConfig config = new();

        config.InputDeviceOrder.Add(Microphone);
        config.Channels.Add(new ChannelConfig { DeviceId = Microphone, Name = "Mayor 180 degrees" });
        config.Buses.Add(new BusConfig { Name = "Stream", Role = BusRole.Stream, ChannelCount = 2 });

        ConsoleFixture console = new(config);

        console.AddDevice(Microphone, 1);
        console.Controller.BindRecording(session);

        return console;
    }

    static string ReadFourCharacterCode(string path, int offset)
    {
        byte[] bytes = File.ReadAllBytes(path);

        return System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
    }

    static float[] ReadSamples(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int at = FindDataChunk(bytes);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
        int count = length / WaveWriter.BytesPerSample;

        float[] samples = new float[count];

        for (int index = 0; index < count; index++)
        {
            int offset = at + 8 + (index * WaveWriter.BytesPerSample);
            int value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

            // Sign-extend from twenty-four bits.
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            samples[index] = value / 8388607f;
        }

        return samples;
    }

    static int FindDataChunk(byte[] bytes)
    {
        for (int at = 12; at + 8 <= bytes.Length;)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));

            if (id == "data")
            {
                return at;
            }

            at += 8 + size + (size & 1);
        }

        throw new InvalidOperationException("The file has no data chunk.");
    }
}
