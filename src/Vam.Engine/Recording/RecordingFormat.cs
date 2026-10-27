namespace Vam.Engine.Recording;

/// <summary>What a recorded track's file looks like.</summary>
/// <remarks>
/// The rate and the channel count come from the engine rather than being chosen, because a
/// recording that resampled on the way to disk would no longer be the raw material this epic exists
/// to preserve.
/// </remarks>
public sealed record RecordingFormat
{
    /// <summary>The rate the engine runs at.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Channels in this track.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Frames the audio thread hands over at once.</summary>
    public required int BlockFrames { get; init; }

    /// <summary>The file the track is written as.</summary>
    public RecordingContainer Container { get; init; } = RecordingContainer.Wave;

    /// <summary>The file name extension for <see cref="Container"/>, with its dot.</summary>
    public string FileExtension => Container == RecordingContainer.Flac ? ".flac" : ".wav";

    /// <summary>How many bytes a track in this format writes over a stretch of time.</summary>
    /// <remarks>
    /// The uncompressed size for FLAC too. How well it compresses depends on the audio, and a FLAC
    /// frame is never meaningfully larger than the same samples uncompressed, so this is the bound.
    /// </remarks>
    /// <param name="duration">How long it records.</param>
    /// <returns>Bytes.</returns>
    public long BytesFor(TimeSpan duration) =>
        (long)(DiskGuard.BytesPerSecond(SampleRate, ChannelCount) * duration.TotalSeconds);

    /// <summary>Creates the file for a track in this format.</summary>
    /// <param name="path">Where to write.</param>
    /// <returns>The writer, with the header already on disk.</returns>
    public ITrackWriter OpenWriter(string path) => Container switch
    {
        RecordingContainer.Wave => new WaveWriter(path, SampleRate, ChannelCount, BlockFrames),
        RecordingContainer.Flac => new FlacWriter(path, SampleRate, ChannelCount, BlockFrames),
        _ => throw new ArgumentOutOfRangeException(nameof(Container), Container, "Not a recording container.")
    };
}
