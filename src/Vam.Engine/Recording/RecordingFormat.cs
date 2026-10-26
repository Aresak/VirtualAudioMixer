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

    /// <summary>How many bytes a track in this format writes over a stretch of time.</summary>
    /// <param name="duration">How long it records.</param>
    /// <returns>Bytes.</returns>
    public long BytesFor(TimeSpan duration) =>
        (long)(DiskGuard.BytesPerSecond(SampleRate, ChannelCount) * duration.TotalSeconds);
}
