namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What to ask a render device for. What is actually granted comes back as
/// <see cref="AudioStreamFormat"/>.
/// </summary>
/// <remarks>
/// A backend grants the rate and the channel count asked for here or it throws, for the same reason
/// as <see cref="CaptureOptions"/> - and with one extra consequence on this side, because the
/// primary output is the master clock. An output opened at a rate nobody asked for does not merely
/// sound wrong, it runs the entire engine at the wrong speed.
/// </remarks>
/// <param name="ShareMode">
/// Preferred share mode. Virtual endpoints are always opened shared - another application has to
/// keep using them at the same time, which is the entire point of them.
/// </param>
/// <param name="BufferDuration">
/// Requested time per callback. The device rounds this to something it can do.
/// </param>
/// <param name="ChannelCount">Channels to render, or 0 to use everything the device offers.</param>
/// <param name="SampleRate">Rate to render at, or 0 to use whatever the device offers.</param>
public readonly record struct RenderOptions(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int ChannelCount = 0,
    int SampleRate = 0);
