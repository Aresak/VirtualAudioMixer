using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// What a stream is being asked for, with the direction taken out of it.
/// </summary>
/// <remarks>
/// <see cref="CaptureOptions"/> and <see cref="RenderOptions"/> say the same four things and stay
/// separate above this line, because a capture option that made sense for a render device would be
/// a mistake the compiler could not catch. Below it there is one negotiation and it does not care
/// which way the audio is going.
/// </remarks>
/// <param name="ShareMode">The share mode to prefer.</param>
/// <param name="BufferDuration">How much time per callback to ask for.</param>
/// <param name="SampleRate">The rate that must be delivered, or 0 to take the device's.</param>
/// <param name="ChannelCount">The width that must be delivered, or 0 to take the device's.</param>
readonly record struct StreamRequest(
    ShareMode ShareMode,
    TimeSpan BufferDuration,
    int SampleRate,
    int ChannelCount);
