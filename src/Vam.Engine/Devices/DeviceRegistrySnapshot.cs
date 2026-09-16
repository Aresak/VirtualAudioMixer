namespace Vam.Engine.Devices;

/// <summary>
/// The whole device-to-strip mapping, in a form that can be written to configuration and read back.
/// </summary>
/// <remarks>
/// Defined here and persisted in EPIC-08. Deliberately nothing but data: it survives a restart, a
/// different machine, and a device being renamed by its driver.
/// </remarks>
/// <param name="Devices">Every remembered device, in no particular order.</param>
public sealed record DeviceRegistrySnapshot(IReadOnlyList<RememberedDevice> Devices)
{
    /// <summary>An empty mapping.</summary>
    public static DeviceRegistrySnapshot Empty { get; } = new([]);
}
