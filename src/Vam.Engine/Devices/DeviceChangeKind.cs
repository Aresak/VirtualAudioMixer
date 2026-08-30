namespace Vam.Engine.Devices;

/// <summary>What happened to a device.</summary>
/// <remarks>
/// Deliberately not an error enum. A microphone that re-enumerates in the middle of a council
/// meeting is a normal event with a normal recovery, and modelling it as a fault is how a session
/// ends up stopping for something it should have shrugged off.
/// </remarks>
public enum DeviceChangeKind
{
    /// <summary>The device is present and usable.</summary>
    Arrived,

    /// <summary>The device is gone. Expected to come back.</summary>
    Removed,

    /// <summary>The device was there but could not be opened. A retry is scheduled.</summary>
    OpenFailed
}
