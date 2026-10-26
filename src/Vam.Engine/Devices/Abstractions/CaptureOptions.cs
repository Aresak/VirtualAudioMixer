namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What to ask a capture device for. What is actually granted comes back as
/// <see cref="AudioStreamFormat"/>.
/// </summary>
/// <remarks>
/// A backend grants the rate and the channel count asked for here or it throws. It may not open the
/// device at something else and report it: a ring built for one rate and fed at another underruns
/// for the length of the session, and no servo has the authority to correct a whole percent.
/// </remarks>
/// <param name="ShareMode">
/// Preferred share mode. A backend that cannot grant <see cref="ShareMode.Exclusive"/> falls back
/// to shared and says so, loudly - it must never be a silent downgrade.
/// </param>
/// <param name="BufferDuration">
/// Requested time per callback. The device rounds this to something it can do.
/// </param>
/// <param name="ChannelCount">Channels to capture, or 0 to take everything the device offers.</param>
/// <param name="SampleRate">
/// Rate to capture at, or 0 to take whatever the device offers. The engine's own rate, in every
/// case where the samples are going into a mix graph.
/// </param>
public readonly record struct CaptureOptions(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int ChannelCount = 0,
    int SampleRate = 0);
