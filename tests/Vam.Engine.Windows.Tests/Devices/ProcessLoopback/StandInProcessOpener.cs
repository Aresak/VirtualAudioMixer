using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>Opens <see cref="StandInCaptureStream"/>s where the WASAPI backend would activate process loopback.</summary>
sealed class StandInProcessOpener : IProcessCaptureOpener
{
    const int DefaultSampleRate = 48000;
    const int BufferFrames = 480;

    readonly List<StandInCaptureStream> opened = [];

    /// <summary>Every capture opened, in order.</summary>
    public IReadOnlyList<StandInCaptureStream> Opened => opened;

    /// <summary>Whether the next open fails, as an activation that times out would.</summary>
    public bool IsFailing { get; set; }

    public ICaptureStream OpenProcess(AudioDeviceId deviceId, ProcessEntry process, CaptureOptions options)
    {
        if (IsFailing)
        {
            throw new TimeoutException($"Process loopback for PID {process.ProcessId} did not activate in time.");
        }

        AudioStreamFormat format = new(
            options.SampleRate > 0 ? options.SampleRate : DefaultSampleRate,
            options.ChannelCount > 0 ? options.ChannelCount : ProcessLoopbackSources.DefaultChannelCount,
            BufferFrames,
            ShareMode.Shared);

        StandInCaptureStream stream = new(deviceId, process, format);

        opened.Add(stream);

        return stream;
    }
}
