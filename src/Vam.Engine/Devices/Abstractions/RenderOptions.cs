namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What to ask a render device for. What is actually granted comes back as
/// <see cref="AudioStreamFormat"/>.
/// </summary>
/// <param name="ShareMode">
/// Preferred share mode. Virtual endpoints are always opened shared - another application has to
/// keep using them at the same time, which is the entire point of them.
/// </param>
/// <param name="BufferDuration">
/// Requested time per callback. The device rounds this to something it can do.
/// </param>
/// <param name="ChannelCount">Channels to render, or 0 to use everything the device offers.</param>
public readonly record struct RenderOptions(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int ChannelCount = 0);
