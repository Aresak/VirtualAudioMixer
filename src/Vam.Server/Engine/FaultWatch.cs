using Microsoft.Extensions.Logging;
using Vam.Engine.Devices;
using Vam.Engine.Devices.Abstractions;
using Vam.Engine.Graph;

namespace Vam.Server.Engine;

/// <summary>
/// Mutes a strip whose device has failed, and says so once. I1.
/// </summary>
/// <remarks>
/// <para>
/// EPIC-12 calls this the single most important behaviour in the project, and it is worth being
/// precise about why: <b>an error inside one strip must never reach the mix.</b> The graph already
/// honours <see cref="ChannelFlags.Faulted"/> — a faulted strip is silent without anybody muting it
/// — but a flag nothing sets is a safety mechanism nothing arms. This is what arms it.
/// </para>
/// <para>
/// <b>Control thread, never the audio thread.</b> A fault is noticed by a device thread, recorded as
/// a state, and acted on here, on the same loop that polls the supervisor. The audio callback never
/// branches on a fault; by the time it renders, the strip is a gain of zero like any other silent
/// one.
/// </para>
/// <para>
/// <b>Absent is not faulted.</b> A device that has been unplugged is a normal event with a normal
/// recovery, and the supervisor is already waiting for it to come back. Muting the strip for it
/// would be right, and logging it as a fault every five seconds would bury the one that mattered.
/// </para>
/// </remarks>
public sealed class FaultWatch(GraphController graph, DeviceInputChannelRegistry channels, ILogger<FaultWatch> logger)
{
    readonly Dictionary<int, DeviceStreamState> reported = [];

    /// <summary>How many strips are currently muted because their device failed.</summary>
    public int FaultedCount { get; private set; }

    /// <summary>
    /// Checks every strip's device and mutes or restores it. Control thread.
    /// </summary>
    /// <remarks>
    /// Cheap enough to run on every control tick: one telemetry read per strip and a comparison.
    /// Nothing is submitted unless something actually changed, so a healthy console costs a loop.
    /// </remarks>
    public void Poll()
    {
        int faulted = 0;

        for (int index = 0; index < graph.Config.Channels.Count && index < channels.Count; index++)
        {
            DeviceStreamState state = channels.Channels[index].GetTelemetry().State;
            bool isBroken = state is DeviceStreamState.Faulted or DeviceStreamState.Absent;

            if (isBroken)
            {
                faulted++;
            }

            Announce(index, state);

            bool wasFlagged = (graph.Config.Channels[index].Flags & ChannelFlags.Faulted) != 0;

            if (wasFlagged == isBroken)
            {
                continue;
            }

            // Through the command queue, so the change lands with every other parameter change on
            // the next published snapshot rather than mutating one the audio thread is reading.
            graph.Submit(GraphCommand.SetFlag(index, ChannelFlags.Faulted, isBroken));
            graph.Pump();
        }

        FaultedCount = faulted;
    }

    void Announce(int index, DeviceStreamState state)
    {
        if (reported.TryGetValue(index, out DeviceStreamState previous) && previous == state)
        {
            return;
        }

        reported[index] = state;

        string name = graph.Config.Channels[index].Name;

        // Once per transition, naming the strip and what happened to it. A device that has failed
        // fails on every block, and a line per block would push the first one - the only one with
        // any information in it - off the top of the log.
        switch (state)
        {
            case DeviceStreamState.Faulted:
                logger.LogError(
                    "{Channel} was muted: its device failed. The session continues without it.",
                    name);
                break;

            case DeviceStreamState.Absent:
                logger.LogWarning(
                    "{Channel} was muted: its device is no longer present. It will come back on its own if the device does.",
                    name);
                break;

            case DeviceStreamState.Running when previous is DeviceStreamState.Faulted or DeviceStreamState.Absent:
                logger.LogInformation("{Channel} is back.", name);
                break;

            default:
                break;
        }
    }
}
