using Vam.Engine.Devices.Abstractions;

namespace Vam.TestKit.Devices;

/// <summary>
/// What a <see cref="NullAudioBackend"/> stream is being asked for, with the direction taken out.
/// </summary>
/// <remarks>
/// <see cref="CaptureOptions"/> and <see cref="RenderOptions"/> say the same four things and stay
/// separate, because a capture option that made sense for a render device would be a mistake the
/// compiler could not catch. The backend grants both the same way, and this is where the two meet.
/// </remarks>
/// <param name="ShareMode">The share mode to prefer.</param>
/// <param name="BufferDuration">How much time per callback to ask for.</param>
/// <param name="SampleRate">The rate that must be delivered, or 0 to take the device's.</param>
/// <param name="ChannelCount">The width that must be delivered, or 0 to take the device's.</param>
public readonly record struct NullStreamRequest(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int SampleRate,
    int ChannelCount);
