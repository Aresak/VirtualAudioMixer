using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// A process capture that holds its source's lease, and gives it back when it closes.
/// </summary>
/// <remarks>
/// Giving it back is what lets a browser restarted under a new PID be captured again. Everything but
/// <see cref="Dispose"/> passes straight through, so nothing here is on the audio path.
/// </remarks>
/// <param name="inner">The capture itself.</param>
/// <param name="lease">The claim on the source.</param>
public sealed class LeasedCaptureStream(ICaptureStream inner, ProcessCaptureLease lease) : ICaptureStream
{
    /// <inheritdoc />
    public AudioDeviceId DeviceId => inner.DeviceId;

    /// <inheritdoc />
    public DeviceDirection Direction => inner.Direction;

    /// <inheritdoc />
    public AudioStreamFormat Format => inner.Format;

    /// <inheritdoc />
    public DeviceStreamState State => inner.State;

    /// <inheritdoc />
    public void Start(CaptureCallback onSamplesCaptured) => inner.Start(onSamplesCaptured);

    /// <inheritdoc />
    public void Stop() => inner.Stop();

    /// <inheritdoc />
    public void Dispose()
    {
        inner.Dispose();
        lease.Dispose();
    }
}
