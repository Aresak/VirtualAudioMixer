using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// Says that one process source is being captured from one PID, until it is disposed.
/// </summary>
/// <remarks>Owned by the capture stream, so closing the stream is what frees the source.</remarks>
public sealed class ProcessCaptureLease : IDisposable
{
    readonly ProcessLoopbackSources owner;
    int isReleased;

    internal ProcessCaptureLease(ProcessLoopbackSources owner, AudioDeviceId deviceId, ProcessEntry process)
    {
        this.owner = owner;
        DeviceId = deviceId;
        Process = process;
    }

    /// <summary>The source.</summary>
    public AudioDeviceId DeviceId { get; }

    /// <summary>The process being captured.</summary>
    public ProcessEntry Process { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isReleased, 1) == 0)
        {
            owner.Release(DeviceId, Process.ProcessId);
        }
    }
}
