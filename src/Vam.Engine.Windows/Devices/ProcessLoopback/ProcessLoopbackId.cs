using System.Globalization;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// A capture source that is a process rather than an endpoint: <c>process:&lt;pid&gt;</c> or
/// <c>process:&lt;executable&gt;</c>.
/// </summary>
/// <remarks>
/// Only the Windows backend reads this. Above <see cref="IAudioBackend"/> it is an ordinary, opaque
/// <see cref="AudioDeviceId"/>.
/// </remarks>
public readonly record struct ProcessLoopbackId
{
    /// <summary>What every process source's identity starts with.</summary>
    public const string Scheme = "process:";

    const string ExecutableExtension = ".exe";

    static readonly char[] NotInAFileName = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    ProcessLoopbackId(int processId, string executableName)
    {
        ProcessId = processId;
        ExecutableName = executableName;
    }

    /// <summary>The PID, or 0 when the source is named by executable.</summary>
    public int ProcessId { get; }

    /// <summary>The executable, always ending in <c>.exe</c>, or empty when the source is named by PID.</summary>
    public string ExecutableName { get; }

    /// <summary>Whether this names one process by its PID.</summary>
    public bool IsByProcessId => ProcessId > 0;

    /// <summary>Reads a device identity as a process source.</summary>
    /// <param name="deviceId">The identity.</param>
    /// <param name="target">The process source, when it is one.</param>
    /// <returns>Whether <paramref name="deviceId"/> is a well-formed process source.</returns>
    public static bool TryParse(AudioDeviceId deviceId, out ProcessLoopbackId target)
    {
        target = default;

        if (deviceId.IsNone || !deviceId.Value.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        string name = deviceId.Value[Scheme.Length..].Trim();

        if (name.Length == 0 || name.IndexOfAny(NotInAFileName) >= 0)
        {
            return false;
        }

        if (name.All(char.IsAsciiDigit))
        {
            bool isProcessId = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
                && processId > 0;

            target = isProcessId ? new ProcessLoopbackId(processId, string.Empty) : default;
            return isProcessId;
        }

        target = new ProcessLoopbackId(
            0,
            name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? name : name + ExecutableExtension);

        return true;
    }

    /// <summary>Finds the process this names among the running ones.</summary>
    /// <remarks>
    /// By executable, it is the root of the tree: a process of that name whose parent is not one too.
    /// Chromium plays audio from a child, and capturing only a child's tree would miss the rest of the
    /// browser. Two roots are two instances, and choosing between them is refused rather than guessed,
    /// unless VAM started one of them itself: then it is that one, and only that one.
    /// </remarks>
    /// <param name="processes">What is running.</param>
    /// <param name="launchedProcessId">The PID VAM started this executable under, or 0.</param>
    /// <returns>The process, or why there is none.</returns>
    public ProcessResolution Resolve(IReadOnlyList<ProcessEntry> processes, int launchedProcessId = 0)
    {
        ArgumentNullException.ThrowIfNull(processes);

        if (IsByProcessId)
        {
            return ResolveProcessId(processes, ProcessId, string.Empty);
        }

        return launchedProcessId > 0
            ? ResolveProcessId(processes, launchedProcessId, ExecutableName)
            : ResolveExecutable(processes);
    }

    /// <summary>What the operator sees this source called.</summary>
    /// <param name="process">The process it resolved to.</param>
    /// <returns>A display name.</returns>
    public string Describe(ProcessEntry process) =>
        IsByProcessId ? $"{process.ExecutableName}, PID {process.ProcessId}" : ExecutableName;

    static ProcessResolution ResolveProcessId(IReadOnlyList<ProcessEntry> processes, int processId, string executableName)
    {
        foreach (ProcessEntry process in processes)
        {
            if (process.ProcessId == processId
                && (executableName.Length == 0
                    || string.Equals(process.ExecutableName, executableName, StringComparison.OrdinalIgnoreCase)))
            {
                return new ProcessResolution(ProcessResolutionOutcome.Found, process, 1);
            }
        }

        return new ProcessResolution(ProcessResolutionOutcome.Missing, default, 0);
    }

    ProcessResolution ResolveExecutable(IReadOnlyList<ProcessEntry> processes)
    {
        string executableName = ExecutableName;
        List<ProcessEntry> named = [.. processes.Where(process =>
            string.Equals(process.ExecutableName, executableName, StringComparison.OrdinalIgnoreCase))];

        HashSet<int> namedIds = [.. named.Select(process => process.ProcessId)];
        List<ProcessEntry> roots = [.. named.Where(process => !namedIds.Contains(process.ParentProcessId))];

        return roots.Count switch
        {
            0 => new ProcessResolution(ProcessResolutionOutcome.Missing, default, 0),
            1 => new ProcessResolution(ProcessResolutionOutcome.Found, roots[0], 1),
            _ => new ProcessResolution(ProcessResolutionOutcome.Ambiguous, default, roots.Count)
        };
    }
}
