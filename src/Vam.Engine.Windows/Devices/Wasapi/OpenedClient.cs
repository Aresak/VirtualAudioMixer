using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// An initialised <c>IAudioClient</c> and the facts about how it was initialised.
/// </summary>
/// <remarks>
/// The format has to travel with the client rather than be read back off it. <c>MixFormat</c> is
/// what shared mode would present, not what this stream was opened with, and reading it back after
/// an exclusive open - or after a shared open that asked the system to convert - gives a format the
/// stream does not actually carry.
/// </remarks>
/// <param name="Client">The initialised client. The caller owns it.</param>
/// <param name="Format">What the client was initialised with.</param>
/// <param name="ShareMode">Which mode it was granted.</param>
/// <param name="DeviceSampleRate">The rate the hardware itself runs at, or 0 when it would not say.</param>
sealed record OpenedClient(
    AudioClient Client,
    WaveFormat Format,
    ShareMode ShareMode,
    int DeviceSampleRate)
{
    /// <summary>Describes this stream the way the engine reads it.</summary>
    /// <returns>The granted format, including the device's own rate.</returns>
    public AudioStreamFormat Describe() =>
        new(Format.SampleRate, Format.Channels, Client.BufferSize, ShareMode, DeviceSampleRate);
}
