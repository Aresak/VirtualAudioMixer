using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;

namespace Vam.Engine.Configuration;

/// <summary>
/// Finds a remembered device again by its name when Windows has given it a new endpoint ID.
/// </summary>
/// <remarks>
/// <para>
/// A USB device moved to another port, or whose driver was reinstalled, comes back under a new ID.
/// It is matched by its exact friendly name and direction, only to a present device nothing else in
/// the console uses, and only when exactly one device and one remembered ID fit. Two identical
/// speakerphones are a guess, and a strip on the wrong microphone is worse than an absent one.
/// </para>
/// <para>Control thread only.</para>
/// </remarks>
public sealed class MovedDeviceFinder(ILogger<MovedDeviceFinder> logger)
{
    readonly HashSet<AudioDeviceId> reportedAmbiguous = [];

    /// <summary>
    /// Records the names of the devices that are present, and re-aims the console at any absent
    /// device that can be found again by name.
    /// </summary>
    /// <param name="config">The console. Changed in place.</param>
    /// <param name="backend">What is plugged in.</param>
    /// <param name="busy">Devices in use that the console no longer names, such as a removed strip's.</param>
    /// <returns>Every device that was re-aimed.</returns>
    public IReadOnlyList<MovedDevice> Follow(
        GraphConfig config,
        IAudioBackend backend,
        IReadOnlyCollection<AudioDeviceId>? busy = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(backend);

        Dictionary<AudioDeviceId, AudioDeviceInfo> present = [];

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Capture))
        {
            present[device.Id] = device;
        }

        foreach (AudioDeviceInfo device in backend.Enumerate(DeviceDirection.Render))
        {
            present[device.Id] = device;
        }

        RememberNames(config, present);

        // A device back under its old ID is no longer ambiguous. If it goes missing again, that
        // absence gets its own warning.
        reportedAmbiguous.RemoveWhere(present.ContainsKey);

        List<Wanted> missing = Missing(config, present);
        HashSet<AudioDeviceId> used = Used(config);

        used.UnionWith(busy ?? []);
        List<MovedDevice> moves = [];

        foreach (Wanted wanted in missing)
        {
            if (TryMatch(wanted, missing, present, used) is { } move)
            {
                Replace(config, move);
                used.Add(move.To);
                moves.Add(move);
            }
        }

        return moves;
    }

    static void RememberNames(GraphConfig config, Dictionary<AudioDeviceId, AudioDeviceInfo> present)
    {
        for (int index = 0; index < config.Channels.Count; index++)
        {
            ChannelConfig channel = config.Channels[index];

            if (present.TryGetValue(channel.DeviceId, out AudioDeviceInfo? device)
                && channel.DeviceName != device.FriendlyName)
            {
                config.Channels[index] = channel with { DeviceName = device.FriendlyName };
            }
        }

        for (int index = 0; index < config.Buses.Count; index++)
        {
            BusConfig bus = config.Buses[index];

            if (present.TryGetValue(bus.OutputDeviceId, out AudioDeviceInfo? device)
                && bus.OutputDeviceName != device.FriendlyName)
            {
                config.Buses[index] = bus with { OutputDeviceName = device.FriendlyName };
            }
        }
    }

    static List<Wanted> Missing(GraphConfig config, Dictionary<AudioDeviceId, AudioDeviceInfo> present)
    {
        List<Wanted> missing = [];

        // A console saved before device names were kept has only the strip's name, which is the
        // device's name unless somebody renamed the strip.
        foreach (ChannelConfig channel in config.Channels)
        {
            string name = channel.DeviceName.Length > 0 ? channel.DeviceName : channel.Name;

            Add(missing, present, channel.DeviceId, DeviceDirection.Capture, name);
        }

        foreach (BusConfig bus in config.Buses)
        {
            Add(missing, present, bus.OutputDeviceId, DeviceDirection.Render, bus.OutputDeviceName);
        }

        return missing;
    }

    static void Add(
        List<Wanted> missing,
        Dictionary<AudioDeviceId, AudioDeviceInfo> present,
        AudioDeviceId deviceId,
        DeviceDirection direction,
        string name)
    {
        if (deviceId.IsNone || name.Length == 0 || present.ContainsKey(deviceId)
            || missing.Exists(wanted => wanted.DeviceId == deviceId))
        {
            return;
        }

        missing.Add(new Wanted(deviceId, direction, name));
    }

    static HashSet<AudioDeviceId> Used(GraphConfig config)
    {
        HashSet<AudioDeviceId> used = [];

        foreach (ChannelConfig channel in config.Channels)
        {
            used.Add(channel.DeviceId);
        }

        foreach (BusConfig bus in config.Buses)
        {
            used.Add(bus.OutputDeviceId);
        }

        return used;
    }

    MovedDevice? TryMatch(
        Wanted wanted,
        List<Wanted> missing,
        Dictionary<AudioDeviceId, AudioDeviceInfo> present,
        HashSet<AudioDeviceId> used)
    {
        List<AudioDeviceInfo> candidates = [.. present.Values.Where(device =>
            device.Direction == wanted.Direction
            && device.FriendlyName == wanted.Name
            && !used.Contains(device.Id))];

        int waiting = missing.Count(other => other.Direction == wanted.Direction && other.Name == wanted.Name);

        if (candidates.Count == 1 && waiting == 1)
        {
            reportedAmbiguous.Remove(wanted.DeviceId);

            logger.LogInformation(
                "{DeviceName} is present under a new endpoint ID. {OldId} is now {NewId}.",
                wanted.Name,
                wanted.DeviceId.Value,
                candidates[0].Id.Value);

            return new MovedDevice(wanted.DeviceId, candidates[0].Id, wanted.Direction, wanted.Name);
        }

        if (candidates.Count > 0 && reportedAmbiguous.Add(wanted.DeviceId))
        {
            logger.LogWarning(
                "{DeviceName} is not present under {OldId}, and {Candidates} unused devices and {Waiting} absent "
                + "ones share that name. It stays absent rather than guessing; assign it by hand.",
                wanted.Name,
                wanted.DeviceId.Value,
                candidates.Count,
                waiting);
        }

        return null;
    }

    static void Replace(GraphConfig config, MovedDevice move)
    {
        for (int index = 0; index < config.Channels.Count; index++)
        {
            if (config.Channels[index].DeviceId == move.From)
            {
                config.Channels[index] = config.Channels[index] with { DeviceId = move.To, DeviceName = move.FriendlyName };
            }
        }

        for (int index = 0; index < config.Buses.Count; index++)
        {
            if (config.Buses[index].OutputDeviceId == move.From)
            {
                config.Buses[index] = config.Buses[index] with
                {
                    OutputDeviceId = move.To,
                    OutputDeviceName = move.FriendlyName
                };
            }
        }

        for (int index = 0; index < config.InputDeviceOrder.Count; index++)
        {
            if (config.InputDeviceOrder[index] == move.From)
            {
                config.InputDeviceOrder[index] = move.To;
            }
        }

        for (int index = 0; index < config.EndpointPairs.Count; index++)
        {
            EndpointPair pair = config.EndpointPairs[index];

            config.EndpointPairs[index] = new EndpointPair(
                pair.CaptureDeviceId == move.From ? move.To : pair.CaptureDeviceId,
                pair.RenderDeviceId == move.From ? move.To : pair.RenderDeviceId);
        }
    }

    readonly record struct Wanted(AudioDeviceId DeviceId, DeviceDirection Direction, string Name);
}
