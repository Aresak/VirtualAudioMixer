namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// What every open device stream has, whichever way the audio is going.
/// </summary>
public interface IAudioStream : IDisposable
{
    /// <summary>The device this stream was opened on.</summary>
    AudioDeviceId DeviceId { get; }

    /// <summary>Capture or render.</summary>
    DeviceDirection Direction { get; }

    /// <summary>
    /// What the device actually granted, which is not necessarily what was asked for.
    /// </summary>
    AudioStreamFormat Format { get; }

    /// <summary>
    /// Current state. Readable from any thread; may be one callback out of date, which is fine
    /// because every decision made from it happens off the audio thread anyway.
    /// </summary>
    DeviceStreamState State { get; }

    /// <summary>
    /// Stops moving audio. Safe to call when already stopped. Never throws because a device
    /// disappeared - that is <see cref="DeviceStreamState.Absent"/>, not an error.
    /// </summary>
    void Stop();
}
