using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>A process list a test writes, so starting and closing a browser is one line.</summary>
sealed class ScriptedProcessSnapshot : IProcessSnapshot
{
    readonly List<ProcessEntry> running = [];

    /// <summary>How many snapshots have been taken, since each is a walk of every process on the machine.</summary>
    public int TakenCount { get; private set; }

    public void Start(int processId, int parentProcessId, string executableName) =>
        running.Add(new ProcessEntry(processId, parentProcessId, executableName));

    public void Exit(int processId) => running.RemoveAll(process => process.ProcessId == processId);

    public IReadOnlyList<ProcessEntry> Take()
    {
        TakenCount++;
        return [.. running];
    }
}
