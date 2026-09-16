using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// The answer to "where is this device".
/// </summary>
/// <remarks>
/// A result rather than an exception, because a missing device is an ordinary event. A Jabra that
/// re-enumerates mid-session must not take the meeting down, so absence has to be something the
/// caller reads and reacts to rather than something it catches.
/// </remarks>
public readonly record struct DeviceResolution
{
    DeviceResolution(DeviceAvailability availability, AudioDeviceInfo? device, RememberedDevice? remembered)
    {
        Availability = availability;
        Device = device;
        Remembered = remembered;
    }

    /// <summary>Whether the device is present, remembered but gone, or unheard of.</summary>
    public DeviceAvailability Availability { get; }

    /// <summary>The live device, when <see cref="Availability"/> is <see cref="DeviceAvailability.Present"/>.</summary>
    public AudioDeviceInfo? Device { get; }

    /// <summary>What was remembered, when the identity is known at all.</summary>
    public RememberedDevice? Remembered { get; }

    /// <summary>Whether the device can be opened right now.</summary>
    public bool IsPresent => Availability == DeviceAvailability.Present;

    /// <summary>
    /// The best name available for display: the live one if the device is here, the last-known one
    /// if it is not, and a placeholder if it was never seen.
    /// </summary>
    public string DisplayName =>
        Device?.FriendlyName ?? Remembered?.LastKnownName ?? "Unknown device";

    /// <summary>The strip this device belongs to, or -1 when it is not remembered.</summary>
    public int StripIndex => Remembered?.StripIndex ?? -1;

    /// <summary>The device is here.</summary>
    /// <param name="device">The live device.</param>
    /// <param name="remembered">What was remembered about it, if anything.</param>
    /// <returns>A present resolution.</returns>
    public static DeviceResolution Present(AudioDeviceInfo device, RememberedDevice? remembered) =>
        new(DeviceAvailability.Present, device, remembered);

    /// <summary>The device is remembered but not currently connected.</summary>
    /// <param name="remembered">What was remembered about it, including its last-known name.</param>
    /// <returns>An absent resolution.</returns>
    public static DeviceResolution Absent(RememberedDevice remembered) =>
        new(DeviceAvailability.Absent, null, remembered);

    /// <summary>The identity has never been seen.</summary>
    /// <returns>An unknown resolution.</returns>
    public static DeviceResolution Unknown() =>
        new(DeviceAvailability.Unknown, null, null);
}
