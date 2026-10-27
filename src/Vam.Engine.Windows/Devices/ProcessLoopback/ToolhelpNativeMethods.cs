using System.Runtime.InteropServices;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>The kernel32 process snapshot calls, which are the only ones that report a parent PID.</summary>
static unsafe partial class ToolhelpNativeMethods
{
    internal const uint SnapProcess = 0x2;
    internal const int MaxPath = 260;
    internal static readonly nint InvalidHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessEntry32
    {
        internal uint Size;
        internal uint Usage;
        internal uint ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId;
        internal uint Threads;
        internal uint ParentProcessId;
        internal int PriorityClassBase;
        internal uint Flags;
        internal fixed char ExeFile[MaxPath];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32First(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32Next(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}
