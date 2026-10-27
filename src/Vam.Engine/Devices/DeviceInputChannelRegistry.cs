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
/// Membership changes on the control thread only. The clock pulls from <see cref="Current"/> on the
/// audio thread, so every change publishes a new array with one write rather than editing the one
/// being read.
/// </para>
/// </remarks>
public sealed class DeviceInputChannelRegistry
{
    DeviceInputChannel[] channels = [];

    /// <summary>How many channels are open.</summary>
    public int Count => Volatile.Read(ref channels).Length;

    /// <summary>
    /// The open channels, in the order they were added.
    /// </summary>
    /// <remarks>
    /// This order is also the order <see cref="GetAllTelemetry"/> writes in, which is how a caller
    /// maps a telemetry row back to a device without the struct having to carry an identifier.
    /// </remarks>
    public IReadOnlyList<DeviceInputChannel> Channels => Volatile.Read(ref channels);

    /// <summary>The open channels as one consistent set, for the audio thread.</summary>
    public ReadOnlySpan<DeviceInputChannel> Current => Volatile.Read(ref channels);

    /// <summary>Adds an open channel.</summary>
    /// <param name="channel">The channel. Adding one twice is a mistake and is refused.</param>
    public void Add(DeviceInputChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (Array.IndexOf(channels, channel) >= 0)
        {
            throw new InvalidOperationException($"Channel for device {channel.DeviceId} is already registered.");
        }

        Volatile.Write(ref channels, [.. channels, channel]);
    }

    /// <summary>Removes a channel that has been closed.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>Whether it was registered.</returns>
    public bool Remove(DeviceInputChannel channel)
    {
        int index = Array.IndexOf(channels, channel);

        if (index < 0)
        {
            return false;
        }

        Volatile.Write(ref channels, [.. channels.AsSpan(0, index), .. channels.AsSpan(index + 1)]);

        return true;
    }

    /// <summary>Puts a channel in another's place, so every other channel keeps its index.</summary>
    /// <param name="existing">The registered channel.</param>
    /// <param name="replacement">What takes its slot.</param>
    /// <returns>Whether <paramref name="existing"/> was registered.</returns>
    public bool Replace(DeviceInputChannel existing, DeviceInputChannel replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        int index = Array.IndexOf(channels, existing);

        if (index < 0 || Array.IndexOf(channels, replacement) >= 0)
        {
            return false;
        }

        DeviceInputChannel[] next = [.. channels];

        next[index] = replacement;
        Volatile.Write(ref channels, next);

        return true;
    }

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
        DeviceInputChannel[] current = Volatile.Read(ref channels);

        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, current.Length);

        for (int index = 0; index < current.Length; index++)
        {
            destination[index] = current[index].GetTelemetry();
        }

        return current.Length;
    }

    /// <summary>
    /// Advances every channel's drift correction by one observation.
    /// </summary>
    /// <param name="elapsed">Time since the previous call.</param>
    public void UpdateCorrections(TimeSpan elapsed)
    {
        foreach (DeviceInputChannel channel in channels)
        {
            channel.UpdateCorrection(elapsed);
        }
    }
}
