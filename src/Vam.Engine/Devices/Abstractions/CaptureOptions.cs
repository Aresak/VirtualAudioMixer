namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What to ask a capture device for. What is actually granted comes back as
/// <see cref="AudioStreamFormat"/>.
/// </summary>
/// <param name="ShareMode">
/// Preferred share mode. A backend that cannot grant <see cref="ShareMode.Exclusive"/> falls back
/// to shared and says so, loudly - it must never be a silent downgrade.
/// </param>
/// <param name="BufferDuration">
/// Requested time per callback. The device rounds this to something it can do.
/// </param>
/// <param name="ChannelCount">Channels to capture, or 0 to take everything the device offers.</param>
public readonly record struct CaptureOptions(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int ChannelCount = 0);
