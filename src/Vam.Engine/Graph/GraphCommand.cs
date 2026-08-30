namespace Vam.Engine.Graph;

/// <summary>
/// One change an operator asked for, on its way to the control thread.
/// </summary>
/// <remarks>
/// <para>
/// A struct, so a queue of them costs nothing per item. That matters: a dragged fader emits fifty a
/// second, and the whole point of queueing rather than applying directly is that the control loop
/// drains all of them and publishes <b>one</b> snapshot.
/// </para>
/// <para>
/// Deliberately a flat shape with unused fields rather than a hierarchy. A polymorphic command
/// would be an allocation and a virtual call for something the control loop switches on once.
/// </para>
/// </remarks>
public readonly record struct GraphCommand
{
    /// <summary>What this changes.</summary>
    public required GraphCommandKind Kind { get; init; }

    /// <summary>Which strip, where the command names one.</summary>
    public int ChannelIndex { get; init; }

    /// <summary>Which bus, where the command names one.</summary>
    public int BusIndex { get; init; }

    /// <summary>A level in decibels, where the command carries one.</summary>
    public double Value { get; init; }

    /// <summary>Whether the thing is being switched on.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Which flag, for <see cref="GraphCommandKind.ChannelFlag"/>.</summary>
    public ChannelFlags Flag { get; init; }

    /// <summary>Moves a fader. B8.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="decibels">Its new position.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetFader(int channelIndex, double decibels) =>
        new() { Kind = GraphCommandKind.ChannelFader, ChannelIndex = channelIndex, Value = decibels };

    /// <summary>Sets an input trim. A8.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="decibels">Its new trim.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetTrim(int channelIndex, double decibels) =>
        new() { Kind = GraphCommandKind.ChannelTrim, ChannelIndex = channelIndex, Value = decibels };

    /// <summary>Sets or clears a strip flag.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="flag">Which flag.</param>
    /// <param name="isEnabled">Whether to set it.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetFlag(int channelIndex, ChannelFlags flag, bool isEnabled) =>
        new()
        {
            Kind = GraphCommandKind.ChannelFlag,
            ChannelIndex = channelIndex,
            Flag = flag,
            IsEnabled = isEnabled
        };

    /// <summary>Sets a bus's level.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="decibels">Its new level.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetBusGain(int busIndex, double decibels) =>
        new() { Kind = GraphCommandKind.BusGain, BusIndex = busIndex, Value = decibels };

    /// <summary>Mutes or unmutes a bus.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="isMuted">Whether it should be silent.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetBusMuted(int busIndex, bool isMuted) =>
        new() { Kind = GraphCommandKind.BusMuted, BusIndex = busIndex, IsEnabled = isMuted };

    /// <summary>Sets one send. D2 and D2a.</summary>
    /// <param name="channelIndex">Which strip.</param>
    /// <param name="busIndex">Which bus.</param>
    /// <param name="isOn">Whether it carries audio.</param>
    /// <param name="decibels">Its level.</param>
    /// <returns>The command.</returns>
    public static GraphCommand SetSend(int channelIndex, int busIndex, bool isOn, double decibels) =>
        new()
        {
            Kind = GraphCommandKind.Send,
            ChannelIndex = channelIndex,
            BusIndex = busIndex,
            IsEnabled = isOn,
            Value = decibels
        };
}
