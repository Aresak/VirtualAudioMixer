using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Server.Tests;

/// <summary>A process list a test writes.</summary>
sealed class ScriptedProcesses : IProcessSnapshot
{
    readonly List<ProcessEntry> running = [];

    public void Start(int processId, int parentProcessId, string executableName) =>
        running.Add(new ProcessEntry(processId, parentProcessId, executableName));

    public IReadOnlyList<ProcessEntry> Take() => [.. running];
}
