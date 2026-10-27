using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// Adds the named process sources to what another backend lists, and opens them.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the WASAPI backend so that listing a source and giving back its lease can be
/// tested without a sound card: both are what a browser restarted mid-meeting depends on.
/// </para>
/// <para>
/// Control thread only; never the audio path.
/// </para>
/// </remarks>
/// <param name="endpoints">The backend for real devices, which this owns.</param>
/// <param name="opener">What opens a capture once a source has resolved to a process.</param>
/// <param name="sources">The named sources.</param>
public sealed class ProcessLoopbackBackend(
    IAudioBackend endpoints,
    IProcessCaptureOpener opener,
    ProcessLoopbackSources sources) : IAudioBackend
{
    /// <inheritdoc />
    public string Id => endpoints.Id;

    /// <inheritdoc />
    public bool CanProvideTimebase => endpoints.CanProvideTimebase;

    /// <inheritdoc />
    public IReadOnlyList<AudioDeviceInfo> Enumerate(DeviceDirection direction)
    {
        IReadOnlyList<AudioDeviceInfo> present = endpoints.Enumerate(direction);

        if (direction != DeviceDirection.Capture || sources.IsEmpty)
        {
            return present;
        }

        // A process plays into the default output's mix, so that is the rate it nominally runs at.
        int nominalSampleRate = endpoints.DefaultDevice(DeviceDirection.Render)?.NominalSampleRate ?? 0;

        return [.. present, .. sources.Describe(nominalSampleRate)];
    }

    /// <inheritdoc />
    public AudioDeviceInfo? DefaultDevice(DeviceDirection direction) => endpoints.DefaultDevice(direction);

    /// <inheritdoc />
    public ICaptureStream OpenCapture(AudioDeviceId deviceId, CaptureOptions options)
    {
        if (!ProcessLoopbackId.TryParse(deviceId, out ProcessLoopbackId target))
        {
            return endpoints.OpenCapture(deviceId, options);
        }

        ProcessCaptureLease lease = sources.Lease(deviceId, target);

        try
        {
            return new LeasedCaptureStream(opener.OpenProcess(deviceId, lease.Process, options), lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IRenderStream OpenRender(AudioDeviceId deviceId, RenderOptions options) =>
        endpoints.OpenRender(deviceId, options);

    /// <inheritdoc />
    public void Forget(AudioDeviceId deviceId)
    {
        sources.Forget(deviceId);
        endpoints.Forget(deviceId);
    }

    /// <inheritdoc />
    public void Dispose() => endpoints.Dispose();
}
