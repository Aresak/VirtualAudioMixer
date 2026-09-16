namespace Vam.Engine.Devices.Abstractions;

/// <summary>What a stream is currently doing.</summary>
public enum DeviceStreamState
{
    /// <summary>Opened but not running. The normal state before <c>Start</c> and after <c>Stop</c>.</summary>
    Stopped,

    /// <summary>Moving audio.</summary>
    Running,

    /// <summary>
    /// The device failed and this stream is finished. A fault never crosses back into the audio
    /// callback: the strip is muted off-thread and the session continues without it.
    /// </summary>
    Faulted,

    /// <summary>
    /// The device is no longer present. Distinct from <see cref="Faulted"/> because it is a normal
    /// event with a normal recovery - the device is expected to come back.
    /// </summary>
    Absent
}
