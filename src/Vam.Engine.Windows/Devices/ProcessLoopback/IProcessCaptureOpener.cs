using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>Opens what one process tree plays, once a source has been resolved to that process.</summary>
public interface IProcessCaptureOpener
{
    /// <summary>Opens a capture of one process and its children.</summary>
    /// <param name="deviceId">The identity it was asked for under.</param>
    /// <param name="process">The process the source resolved to.</param>
    /// <param name="options">What the engine needs.</param>
    /// <returns>A stopped stream.</returns>
    ICaptureStream OpenProcess(AudioDeviceId deviceId, ProcessEntry process, CaptureOptions options);
}
