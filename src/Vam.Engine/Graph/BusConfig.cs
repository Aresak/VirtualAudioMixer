using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Graph;

/// <summary>
/// One bus, as the operator configured it.
/// </summary>
/// <remarks>
/// A monitor is one of these with a different role, not a different type. That is what makes "add a
/// bus" and "add a monitor" one code path, and it is the reason the role changes only three
/// behaviours rather than branching the whole graph.
/// </remarks>
public sealed record BusConfig
{
    /// <summary>What to call it on the console.</summary>
    public required string Name { get; init; }

    /// <summary>What it is for. Output, monitor or stream.</summary>
    public required BusRole Role { get; init; }

    /// <summary>Channels the bus carries.</summary>
    public int ChannelCount { get; init; } = 2;

    /// <summary>The bus's own level.</summary>
    public double GainDb { get; init; }

    /// <summary>Whether it outputs silence.</summary>
    public bool IsMuted { get; init; }

    /// <summary>
    /// Where it goes. None for a bus that only exists to be recorded or monitored elsewhere.
    /// </summary>
    public AudioDeviceId OutputDeviceId { get; init; } = AudioDeviceId.None;
}
