namespace Vam.Engine.Devices;

/// <summary>
/// The live input channels, and the one place that polls all of their telemetry at once.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="DeviceRegistry"/>, which is about identity: which endpoint belongs to
/// which strip, and what a device was called before it was unplugged. That registry outlives any
/// stream and holds no audio state. This one holds the running channels, and merging the two would
/// give a configuration lookup a dependency on ring buffers.
/// </para>
/// <para>
/// Control thread only. Membership changes when a device is opened or lost, which is the
/// supervisor's business and never the audio thread's.
/// </para>
/// </remarks>
public sealed class DeviceInputChannelRegistry
{
    readonly List<DeviceInputChannel> channels = [];

    /// <summary>How many channels are open.</summary>
    public int Count => channels.Count;

    /// <summary>
    /// The open channels, in the order they were added.
    /// </summary>
    /// <remarks>
    /// This order is also the order <see cref="GetAllTelemetry"/> writes in, which is how a caller
    /// maps a telemetry row back to a device without the struct having to carry an identifier.
    /// </remarks>
    public IReadOnlyList<DeviceInputChannel> Channels => channels;

    /// <summary>Adds an open channel.</summary>
    /// <param name="channel">The channel. Adding one twice is a mistake and is refused.</param>
    public void Add(DeviceInputChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channels.Contains(channel))
        {
            throw new InvalidOperationException($"Channel for device {channel.DeviceId} is already registered.");
        }

        channels.Add(channel);
    }

    /// <summary>Removes a channel that has been closed.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Remove(DeviceInputChannel channel) => channels.Remove(channel);

    /// <summary>
    /// Reads every channel's telemetry into a buffer the caller already owns.
    /// </summary>
    /// <remarks>
    /// A span rather than a returned collection because this is polled at meter rate. Handing back
    /// a fresh list twenty-five times a second would be the only allocation in the whole telemetry
    /// path, and it would be in the hot part of it.
    /// </remarks>
    /// <param name="destination">
    /// Where to write. Must have room for <see cref="Count"/> entries; a short span is a caller bug
    /// rather than a reason to quietly report fewer devices than exist.
    /// </param>
    /// <returns>Entries written.</returns>
    public int GetAllTelemetry(Span<DeviceTelemetry> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, channels.Count);

        for (int index = 0; index < channels.Count; index++)
        {
            destination[index] = channels[index].GetTelemetry();
        }

        return channels.Count;
    }

    /// <summary>
    /// Advances every channel's drift correction by one observation.
    /// </summary>
    /// <param name="elapsed">Time since the previous call.</param>
    public void UpdateCorrections(TimeSpan elapsed)
    {
        for (int index = 0; index < channels.Count; index++)
        {
            channels[index].UpdateCorrection(elapsed);
        }
    }
}
