using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Engine.Windows.Tests.Devices.ProcessLoopback;

/// <summary>A process list a test writes, so starting and closing a browser is one line.</summary>
sealed class ScriptedProcessSnapshot : IProcessSnapshot
{
    readonly List<ProcessEntry> running = [];

    public void Start(int processId, int parentProcessId, string executableName) =>
        running.Add(new ProcessEntry(processId, parentProcessId, executableName));

    public void Exit(int processId) => running.RemoveAll(process => process.ProcessId == processId);

    public IReadOnlyList<ProcessEntry> Take() => [.. running];
}
