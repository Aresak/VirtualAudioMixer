using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// One device the supervisor is responsible for keeping open, and how hard it is currently trying.
/// </summary>
/// <remarks>
/// The backoff lives here rather than in the supervisor because it is per device. One flapping
/// microphone must not slow down the reattachment of the one next to it, which is exactly what a
/// single shared retry timer would do.
/// </remarks>
sealed class TrackedCaptureDevice(
    AudioDeviceId deviceId,
    DeviceInputChannel channel,
    CaptureOptions captureOptions)
{
    /// <summary>First wait after a failed open.</summary>
    static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Longest wait between attempts. Capped rather than doubling forever: a device that has been
    /// gone for an hour should still come back within seconds of being plugged in, and an
    /// unbounded backoff turns a recoverable session into one that needs restarting.
    /// </summary>
    static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(8);

    TimeSpan backoff = FirstBackoff;
    TimeSpan remaining = TimeSpan.Zero;

    /// <summary>Which device.</summary>
    public AudioDeviceId DeviceId => deviceId;

    /// <summary>The channel it feeds. Outlives the stream, so a strip keeps its identity across a gap.</summary>
    public DeviceInputChannel Channel => channel;

    /// <summary>What to ask for when opening.</summary>
    public CaptureOptions CaptureOptions => captureOptions;

    /// <summary>The open stream, or null when the device is missing.</summary>
    public ICaptureStream? Stream { get; set; }

    /// <summary>What the device is called, remembered so a removal can be logged in words.</summary>
    public string FriendlyName { get; set; } = deviceId.Value;

    /// <summary>Whether a stream is currently open.</summary>
    public bool IsOpen => Stream is not null;

    /// <summary>Failed attempts since the last success.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Returns the loop to its shortest wait, after a successful open.</summary>
    public void Succeeded()
    {
        backoff = FirstBackoff;
        remaining = TimeSpan.Zero;
        FailedAttempts = 0;
    }

    /// <summary>Records a failed open and schedules the next attempt.</summary>
    public void Failed()
    {
        FailedAttempts++;
        remaining = backoff;

        TimeSpan doubled = backoff + backoff;
        backoff = doubled > MaximumBackoff ? MaximumBackoff : doubled;
    }

    /// <summary>Stops any scheduled retry, without touching the backoff.</summary>
    public void CancelRetry() => remaining = TimeSpan.Zero;

    /// <summary>
    /// Advances the retry timer.
    /// </summary>
    /// <param name="elapsed">Time since the previous call.</param>
    /// <returns>Whether another attempt is due now.</returns>
    public bool IsRetryDue(TimeSpan elapsed)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return true;
        }

        remaining -= elapsed;

        return remaining <= TimeSpan.Zero;
    }
}
