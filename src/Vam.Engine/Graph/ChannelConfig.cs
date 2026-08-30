using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Graph;

/// <summary>
/// One input strip, as the operator configured it.
/// </summary>
/// <remarks>
/// Levels are in decibels here and linear in <see cref="ChannelParams"/>. The conversion happens
/// once, in the compiler, because a decibel is what a person adjusts and a multiply is what the
/// audio thread wants.
/// </remarks>
public sealed record ChannelConfig
{
    /// <summary>The device this strip listens to.</summary>
    public required AudioDeviceId DeviceId { get; init; }

    /// <summary>What to call it on the console.</summary>
    public required string Name { get; init; }

    /// <summary>Channels the strip carries after any fold.</summary>
    public int ChannelCount { get; init; } = 1;

    /// <summary>Input trim. A8.</summary>
    public double TrimDb { get; init; }

    /// <summary>Fader position. B8.</summary>
    public double FaderDb { get; init; }

    /// <summary>Mute, solo, polarity and mono fold.</summary>
    public ChannelFlags Flags { get; init; } = ChannelFlags.None;
}
