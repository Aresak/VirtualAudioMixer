using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;

namespace Vam.Engine.Devices;

/// <summary>
/// Points a loaded console at devices that came back under a new endpoint ID, and remembers the
/// name of every device it can see. #64.
/// </summary>
/// <remarks>
/// Runs once, on the loaded console, before anything is opened. After this the strips and buses
/// name the IDs that are present, so the supervisor, the clock and the bus outputs open them
/// directly and the next save carries the new IDs.
/// </remarks>
public static class DeviceRebinder
{
    /// <summary>Rebinds and records names in place.</summary>
    /// <param name="config">The console, as loaded.</param>
    /// <param name="backend">What is plugged in.</param>
    /// <param name="logger">Where each rebind, and each refusal to guess, is said.</param>
    /// <returns>How many strips and buses were rebound.</returns>
    public static int Apply(GraphConfig config, IAudioBackend backend, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        return ApplyToChannels(config, backend.Enumerate(DeviceDirection.Capture), logger)
            + ApplyToBuses(config, backend.Enumerate(DeviceDirection.Render), logger);
    }

    static int ApplyToChannels(GraphConfig config, IReadOnlyList<AudioDeviceInfo> present, ILogger logger)
    {
        HashSet<AudioDeviceId> taken = [.. config.Channels.Select(static channel => channel.DeviceId)];
        int rebound = 0;

        for (int index = 0; index < config.Channels.Count; index++)
        {
            ChannelConfig channel = config.Channels[index];

            if (Find(present, channel.DeviceId) is { } same)
            {
                config.Channels[index] = channel with { DeviceName = same.FriendlyName };
                continue;
            }

            // A console saved before device names were kept has only the strip's name, which is
            // the device's name until somebody renames the strip.
            string name = channel.DeviceName.Length > 0 ? channel.DeviceName : channel.Name;
            AudioDeviceInfo? moved = DeviceMatch.Find(name, present, taken.Contains, out int candidates);

            if (moved is null)
            {
                ReportAmbiguity(logger, name, candidates);
                continue;
            }

            config.Channels[index] = channel with { DeviceId = moved.Id, DeviceName = moved.FriendlyName };
            ReplaceInOrder(config.InputDeviceOrder, channel.DeviceId, moved.Id);
            taken.Add(moved.Id);
            rebound++;

            LogRebind(logger, moved.FriendlyName, channel.DeviceId, moved.Id);
        }

        return rebound;
    }

    static int ApplyToBuses(GraphConfig config, IReadOnlyList<AudioDeviceInfo> present, ILogger logger)
    {
        HashSet<AudioDeviceId> taken = [.. config.Buses.Select(static bus => bus.OutputDeviceId)];
        int rebound = 0;

        for (int index = 0; index < config.Buses.Count; index++)
        {
            BusConfig bus = config.Buses[index];

            if (bus.OutputDeviceId.IsNone)
            {
                continue;
            }

            if (Find(present, bus.OutputDeviceId) is { } same)
            {
                config.Buses[index] = bus with { OutputDeviceName = same.FriendlyName };
                continue;
            }

            // No fallback to the bus's own name. "Stream" is never a device.
            AudioDeviceInfo? moved = DeviceMatch.Find(bus.OutputDeviceName, present, taken.Contains, out int candidates);

            if (moved is null)
            {
                ReportAmbiguity(logger, bus.OutputDeviceName, candidates);
                continue;
            }

            config.Buses[index] = bus with { OutputDeviceId = moved.Id, OutputDeviceName = moved.FriendlyName };
            taken.Add(moved.Id);
            rebound++;

            LogRebind(logger, moved.FriendlyName, bus.OutputDeviceId, moved.Id);
        }

        return rebound;
    }

    static AudioDeviceInfo? Find(IReadOnlyList<AudioDeviceInfo> present, AudioDeviceId deviceId)
    {
        foreach (AudioDeviceInfo device in present)
        {
            if (device.Id == deviceId)
            {
                return device;
            }
        }

        return null;
    }

    static void ReplaceInOrder(List<AudioDeviceId> order, AudioDeviceId from, AudioDeviceId to)
    {
        int at = order.IndexOf(from);

        if (at >= 0)
        {
            order[at] = to;
        }
    }

    static void ReportAmbiguity(ILogger logger, string name, int candidates)
    {
        if (candidates > 1)
        {
            logger.LogWarning(
                "{Name} is not present under its saved ID, and {Count} devices carry that name. "
                + "It stays unbound rather than guessing which one.",
                name,
                candidates);
        }
    }

    static void LogRebind(ILogger logger, string name, AudioDeviceId from, AudioDeviceId to) =>
        logger.LogInformation(
            "{Name} came back under a new endpoint ID: {From} is now {To}.",
            name,
            from.Value,
            to.Value);
}
