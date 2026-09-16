using Vam.Core;

namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// Thrown when a device offers a sample format the engine has no conversion for.
/// </summary>
/// <remarks>
/// Raised when the stream opens, never once audio is running. Discovering inside a callback that a
/// conversion was never written would mean throwing on the one thread that must not throw, so the
/// format is decided and refused up front where a caller can still do something about it.
/// </remarks>
public sealed class UnsupportedAudioFormatException : VamException
{
    /// <summary>Describes the format that cannot be read.</summary>
    /// <param name="message">What the device offered, and what is supported instead.</param>
    public UnsupportedAudioFormatException(string message)
        : base(message)
    {
    }
}
