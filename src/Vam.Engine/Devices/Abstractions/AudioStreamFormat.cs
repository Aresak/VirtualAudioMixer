namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// The sample format a stream actually opened with.
/// </summary>
/// <remarks>
/// What was asked for and what was granted are different things. A device may refuse exclusive
/// mode, or refuse a buffer duration, and the stream reports here what it really got - silently
/// accepting a different figure is how a session ends up later than it was in rehearsal.
/// </remarks>
/// <param name="SampleRate">Nominal frames per second, as the device describes itself.</param>
/// <param name="ChannelCount">Channels interleaved in each buffer.</param>
/// <param name="BufferFrames">Frames per callback.</param>
/// <param name="ShareMode">The share mode actually granted.</param>
public readonly record struct AudioStreamFormat(
    int SampleRate,
    int ChannelCount,
    int BufferFrames,
    ShareMode ShareMode);
