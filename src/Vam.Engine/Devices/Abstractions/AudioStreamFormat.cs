namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// The sample format a stream actually opened with.
/// </summary>
/// <remarks>
/// What was asked for and what was granted are different things. A device may refuse exclusive
/// mode, or refuse a buffer duration, and the stream reports here what it really got - silently
/// accepting a different figure is how a session ends up later than it was in rehearsal.
/// </remarks>
/// <param name="SampleRate">Frames per second this stream delivers, which is what the engine asked for.</param>
/// <param name="ChannelCount">Channels interleaved in each buffer.</param>
/// <param name="BufferFrames">Frames per callback.</param>
/// <param name="ShareMode">The share mode actually granted.</param>
/// <param name="DeviceSampleRate">
/// The rate the hardware itself runs at, or zero when the backend cannot say.
/// <para>
/// Not the same question as <paramref name="SampleRate"/>, and the gap between them is the whole of
/// the 2026-09-16 decision: a conference speakerphone whose own converter runs at 16 kHz is opened
/// shared at 48 kHz and the operating system converts in between. An operator is entitled to know
/// which of their microphones that is happening to.
/// </para>
/// </param>
public readonly record struct AudioStreamFormat(
    int SampleRate,
    int ChannelCount,
    int BufferFrames,
    ShareMode ShareMode,
    int DeviceSampleRate = 0)
{
    /// <summary>Whether the operating system is converting between the device's rate and this one.</summary>
    /// <remarks>
    /// False when the backend does not publish a device rate. Unknown and "not converting" are not
    /// the same thing, but a claim that something is wrong is worth more evidence than a claim that
    /// nothing is.
    /// </remarks>
    public bool IsSystemConverting => DeviceSampleRate > 0 && DeviceSampleRate != SampleRate;
}
