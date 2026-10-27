using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// The process sources somebody has named, and whether each is present.
/// </summary>
/// <remarks>
/// <para>
/// A source is listed once it has been asked for, only while its process runs, and until no strip
/// uses it. Listing every process on the machine would be the console picker this does not build;
/// listing a named source only while it runs is what lets the supervisor treat a closed browser as
/// an unplugged device.
/// </para>
/// <para>
/// Control thread and service threads; never the audio path.
/// </para>
/// </remarks>
/// <param name="snapshot">What is running.</param>
/// <param name="logger">Where an ambiguous name is reported.</param>
public sealed class ProcessLoopbackSources(IProcessSnapshot snapshot, ILogger logger)
{
    /// <summary>What the source carries unless asked otherwise. Windows converts whatever the process plays.</summary>
    public const int DefaultChannelCount = 2;

    readonly Lock gate = new();
    readonly Dictionary<AudioDeviceId, ProcessLoopbackId> named = [];
    readonly Dictionary<AudioDeviceId, int> captured = [];
    readonly Dictionary<AudioDeviceId, string> executables = [];

    /// <summary>Whether no source is named, either yet or any more.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (gate)
            {
                return named.Count == 0;
            }
        }
    }

    /// <summary>Lists the named sources whose process is running now.</summary>
    /// <param name="nominalSampleRate">The rate to report them at.</param>
    /// <returns>One capture device per present source.</returns>
    public IReadOnlyList<AudioDeviceInfo> Describe(int nominalSampleRate)
    {
        if (IsEmpty)
        {
            return [];
        }

        IReadOnlyList<ProcessEntry> processes = snapshot.Take();
        List<AudioDeviceInfo> present = [];

        lock (gate)
        {
            foreach ((AudioDeviceId deviceId, ProcessLoopbackId target) in named)
            {
                ProcessResolution resolution = target.Resolve(processes);

                // A capture still bound to a PID that is no longer the one named has outlived its
                // process: absent until it is closed, so the next open follows the new one.
                if (resolution.Outcome != ProcessResolutionOutcome.Found
                    || !IsSameProgram(deviceId, resolution.Process)
                    || (captured.TryGetValue(deviceId, out int processId) && processId != resolution.Process.ProcessId))
                {
                    continue;
                }

                present.Add(new AudioDeviceInfo(
                    deviceId,
                    target.Describe(resolution.Process),
                    DeviceDirection.Capture,
                    DefaultChannelCount,
                    nominalSampleRate,
                    SupportsExclusiveMode: false));
            }
        }

        return present;
    }

    /// <summary>Names a source, finds its process, and claims it for one capture.</summary>
    /// <param name="deviceId">The identity it was asked for under.</param>
    /// <param name="target">That identity, parsed.</param>
    /// <returns>The claim, which the capture stream disposes when it closes.</returns>
    /// <exception cref="DeviceNotFoundException">The process is not running, or the name fits more than one.</exception>
    public ProcessCaptureLease Lease(AudioDeviceId deviceId, ProcessLoopbackId target)
    {
        lock (gate)
        {
            named[deviceId] = target;
        }

        ProcessResolution resolution = target.Resolve(snapshot.Take());

        if (resolution.Outcome == ProcessResolutionOutcome.Ambiguous)
        {
            logger.LogWarning(
                "{DeviceId} fits {Candidates} separate process trees, so none is captured. Start the meeting "
                + "browser as its own instance, or name it by PID.",
                deviceId.Value,
                resolution.Candidates);
        }

        if (resolution.Outcome != ProcessResolutionOutcome.Found)
        {
            throw new DeviceNotFoundException(deviceId);
        }

        lock (gate)
        {
            if (!IsSameProgram(deviceId, resolution.Process))
            {
                throw new DeviceNotFoundException(deviceId);
            }

            if (!captured.TryAdd(deviceId, resolution.Process.ProcessId))
            {
                throw new InvalidOperationException($"{deviceId} is already being captured.");
            }

            executables.TryAdd(deviceId, resolution.Process.ExecutableName);
        }

        return new ProcessCaptureLease(this, deviceId, resolution.Process);
    }

    /// <summary>Stops listing a source, because no strip uses it any more.</summary>
    /// <remarks>
    /// A capture still open keeps its claim until its lease is disposed. Once nothing is named, listing
    /// takes no process snapshot at all.
    /// </remarks>
    /// <param name="deviceId">The identity it was named under.</param>
    public void Forget(AudioDeviceId deviceId)
    {
        lock (gate)
        {
            named.Remove(deviceId);
            executables.Remove(deviceId);
        }
    }

    // Windows reuses PIDs. A PID that now belongs to a different executable is not the process that
    // was named, and following it would put an unrelated program on the strip.
    bool IsSameProgram(AudioDeviceId deviceId, ProcessEntry process) =>
        !executables.TryGetValue(deviceId, out string? executableName)
        || string.Equals(executableName, process.ExecutableName, StringComparison.OrdinalIgnoreCase);

    internal void Release(AudioDeviceId deviceId, int processId)
    {
        lock (gate)
        {
            if (captured.TryGetValue(deviceId, out int held) && held == processId)
            {
                captured.Remove(deviceId);
            }
        }
    }
}
