using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>A process capture with no WASAPI behind it, which remembers which process it was opened on.</summary>
sealed class StandInCaptureStream(AudioDeviceId deviceId, ProcessEntry process, AudioStreamFormat format) : ICaptureStream
{
    public AudioDeviceId DeviceId { get; } = deviceId;

    public ProcessEntry Process { get; } = process;

    public DeviceDirection Direction => DeviceDirection.Capture;

    public AudioStreamFormat Format { get; } = format;

    public DeviceStreamState State { get; private set; } = DeviceStreamState.Stopped;

    public bool IsDisposed { get; private set; }

    public void Start(CaptureCallback onSamplesCaptured) => State = DeviceStreamState.Running;

    public void Stop() => State = DeviceStreamState.Stopped;

    public void Dispose()
    {
        Stop();
        IsDisposed = true;
    }
}
