using System.Runtime.InteropServices;

namespace Vam.Engine.Windows.Shell;

/// <summary>The user32 and shell32 calls the tray needs, and nothing else.</summary>
static unsafe partial class NativeMethods
{
    internal const uint WmNull = 0x0000;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmTimer = 0x0113;
    internal const uint WmLButtonDblClk = 0x0203;
    internal const uint WmRButtonUp = 0x0205;
    internal const uint WmContextMenu = 0x007B;
    internal const uint WmApp = 0x8000;

    internal const uint NimAdd = 0;
    internal const uint NimModify = 1;
    internal const uint NimDelete = 2;

    internal const uint NifMessage = 0x1;
    internal const uint NifIcon = 0x2;
    internal const uint NifTip = 0x4;
    internal const uint NifInfo = 0x10;

    internal const uint MfString = 0x0;
    internal const uint MfSeparator = 0x800;
    internal const uint TpmRightButton = 0x2;
    internal const uint TpmReturnCmd = 0x100;

    internal const uint MbYesNo = 0x4;
    internal const uint MbIconWarning = 0x30;
    internal const uint MbDefButton2 = 0x100;
    internal const uint MbSetForeground = 0x10000;
    internal const int IdYes = 6;

    internal const int SmCxSmIcon = 49;
    internal const int SwRestore = 9;
    internal const nint GwlpUserData = -21;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint Icon;
        internal fixed char Tip[128];
        internal uint State;
        internal uint StateMask;
        internal fixed char Info[256];
        internal uint Version;
        internal fixed char InfoTitle[64];
        internal uint InfoFlags;
        internal Guid Item;
        internal nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal delegate* unmanaged<nint, uint, nint, nint, nint> Procedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal char* MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenPoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nint WParam;
        internal nint LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShellNotifyIcon(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClass(WindowClass* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClass(string className, nint instance);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true
    )]
    internal static partial nint CreateWindow(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint window, nint index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, nint index);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(Message* message, nint window, uint first, uint last);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(Message* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(Message* message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    internal static partial nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint window, nuint id);

    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    internal static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out ScreenPoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint owner, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    internal static partial nint CreateIconFromResourceEx(
        byte* bits,
        uint size,
        [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version,
        int width,
        int height,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint icon);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    internal static partial nint GetModuleHandle(nint name);
}
