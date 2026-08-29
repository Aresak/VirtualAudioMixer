namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// A source of audio devices.
/// </summary>
/// <remarks>
/// The seam that keeps the rest of the engine from knowing what an operating system is. WASAPI is
/// one implementation; a file-driven one makes the whole engine soak-testable with no hardware,
/// and that is not a lesser use of this interface but the main one.
/// </remarks>
public interface IAudioBackend : IDisposable
{
    /// <summary>Short stable identifier for this backend, such as <c>wasapi</c> or <c>offline</c>.</summary>
    string Id { get; }

    /// <summary>
    /// Whether this backend can drive the engine clock. A backend of file sources cannot, so
    /// something else has to keep time.
    /// </summary>
    bool CanProvideTimebase { get; }

    /// <summary>Lists the devices currently present in one direction.</summary>
    /// <param name="direction">Which way to look.</param>
    /// <returns>What is present now. Devices come and go; this is a snapshot, not a subscription.</returns>
    IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction);

    /// <summary>Opens a capture device.</summary>
    /// <param name="deviceId">Which device.</param>
    /// <param name="options">What to ask for.</param>
    /// <returns>A stopped stream. Call <see cref="ICaptureStream.Start"/> to begin.</returns>
    /// <exception cref="DeviceNotFoundException">No device with that identity is present.</exception>
    ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options);

    /// <summary>Opens a render device.</summary>
    /// <param name="deviceId">Which device.</param>
    /// <param name="options">What to ask for.</param>
    /// <returns>A stopped stream. Call <see cref="IRenderStream.Start"/> to begin.</returns>
    /// <exception cref="DeviceNotFoundException">No device with that identity is present.</exception>
    IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options);
}
