using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// A device the configuration expects to see, and the strip it belongs to.
/// </summary>
/// <remarks>
/// This is the serialisable shape. Persisting it is EPIC-08's job; defining it is this one's.
/// </remarks>
/// <param name="Id">
/// Stable identity, from the backend's endpoint identifier. Never an index, never a name.
/// </param>
/// <param name="LastKnownName">
/// What the device was called when it was last seen. Kept only so the interface can say
/// <i>which</i> device is missing - "Jabra Speak 750 is not connected" rather than "a device is
/// not connected". Never used to match.
/// </param>
/// <param name="Direction">Capture or render.</param>
/// <param name="StripIndex">The strip this device feeds, or is fed by.</param>
public sealed record RememberedDevice(
    AudioDeviceId Id,
    string LastKnownName,
    DeviceDirection Direction,
    int StripIndex);
