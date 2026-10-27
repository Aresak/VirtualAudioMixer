using System.ComponentModel;
using System.Runtime.InteropServices;
using static Vam.Engine.Windows.Devices.ProcessLoopback.ToolhelpNativeMethods;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>The running processes, from a Toolhelp snapshot.</summary>
/// <remarks>
/// Toolhelp rather than <c>System.Diagnostics.Process</c>, which does not report a parent, and the
/// parent is how a browser's main process is told from its children.
/// </remarks>
public sealed class ToolhelpProcessSnapshot : IProcessSnapshot
{
    /// <inheritdoc />
    public unsafe IReadOnlyList<ProcessEntry> Take()
    {
        nint snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);

        if (snapshot == InvalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            List<ProcessEntry> processes = [];
            ProcessEntry32 entry = new() { Size = (uint)sizeof(ProcessEntry32) };

            for (bool more = Process32First(snapshot, &entry); more; more = Process32Next(snapshot, &entry))
            {
                processes.Add(new ProcessEntry(
                    (int)entry.ProcessId,
                    (int)entry.ParentProcessId,
                    MemoryMarshal.CreateReadOnlySpanFromNullTerminated(entry.ExeFile).ToString()));
            }

            return processes;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }
}
