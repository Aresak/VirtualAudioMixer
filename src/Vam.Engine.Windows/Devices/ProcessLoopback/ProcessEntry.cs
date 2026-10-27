namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>One running process, as a process snapshot reports it.</summary>
/// <param name="ProcessId">Its PID.</param>
/// <param name="ParentProcessId">The PID of the process that started it.</param>
/// <param name="ExecutableName">Its executable's file name, such as <c>msedge.exe</c>.</param>
public readonly record struct ProcessEntry(int ProcessId, int ParentProcessId, string ExecutableName);
