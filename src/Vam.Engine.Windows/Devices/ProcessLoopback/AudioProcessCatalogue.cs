using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>
/// The running programs that have audio open, by executable, for a console to offer as sources.
/// </summary>
/// <remarks>
/// <para>
/// By executable rather than PID, so a strip set up this way survives the browser being restarted.
/// A browser plays from a child process with the browser's own executable, and
/// <see cref="ProcessLoopbackId"/> resolves that executable back to the root of the tree.
/// </para>
/// <para>
/// Service threads only; never the audio path.
/// </para>
/// </remarks>
/// <param name="sessions">The audio sessions open now.</param>
/// <param name="processes">What is running.</param>
/// <param name="ownProcessId">This engine, which is never offered as its own source.</param>
public sealed class AudioProcessCatalogue(IAudioSessionSnapshot sessions, IProcessSnapshot processes, int ownProcessId)
{
    const string ExecutableExtension = ".exe";

    /// <summary>Lists the programs with audio, playing ones first, then by name.</summary>
    /// <returns>One entry per executable.</returns>
    public IReadOnlyList<AudioProcess> List()
    {
        IReadOnlyList<AudioSessionEntry> open = sessions.Take();

        if (open.Count == 0)
        {
            return [];
        }

        Dictionary<int, string> executables = [];

        foreach (ProcessEntry process in processes.Take())
        {
            executables[process.ProcessId] = process.ExecutableName;
        }

        Dictionary<string, bool> playing = new(StringComparer.OrdinalIgnoreCase);

        foreach (AudioSessionEntry session in open)
        {
            // A session can outlive its process, and the PID may already belong to something else.
            if (session.ProcessId == 0
                || session.ProcessId == ownProcessId
                || !executables.TryGetValue(session.ProcessId, out string? executable)
                || executable.Length == 0)
            {
                continue;
            }

            playing[executable] = session.IsActive || playing.GetValueOrDefault(executable);
        }

        return
        [
            .. playing
                .Select(entry => Describe(entry.Key, entry.Value))
                .OrderByDescending(process => process.IsPlaying)
                .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    static AudioProcess Describe(string executable, bool isPlaying)
    {
        string name = executable.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase)
            ? executable[..^ExecutableExtension.Length]
            : executable;

        return new AudioProcess(new AudioDeviceId(ProcessLoopbackId.Scheme + executable), name, isPlaying);
    }
}
