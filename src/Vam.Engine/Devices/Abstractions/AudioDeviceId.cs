namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// A device's stable identity.
/// </summary>
/// <remarks>
/// Wraps whatever opaque string the backend uses as a permanent endpoint identifier. Never an
/// index and never a friendly name: an index changes the moment something is unplugged, and two
/// identical microphones share a name. This room has two identical Jabras, which breaks both.
/// </remarks>
/// <param name="Value">The backend's opaque identifier. Compared ordinally.</param>
public readonly record struct AudioDeviceId(string Value)
{
    /// <summary>No device.</summary>
    public static AudioDeviceId None => new(string.Empty);

    /// <summary>Whether this identifies a device at all.</summary>
    public bool IsNone => string.IsNullOrEmpty(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
