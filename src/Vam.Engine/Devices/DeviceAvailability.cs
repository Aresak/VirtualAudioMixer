namespace Vam.Engine.Devices;

/// <summary>What the registry knows about a device identity right now.</summary>
public enum DeviceAvailability
{
    /// <summary>Never seen. Not remembered, not present.</summary>
    Unknown,

    /// <summary>
    /// Remembered, but not currently plugged in. A normal event with a normal recovery, not an
    /// error - which is why it has a name rather than an exception.
    /// </summary>
    Absent,

    /// <summary>Present and openable.</summary>
    Present
}
