using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using static Vam.Engine.Windows.Shell.NativeMethods;

namespace Vam.Engine.Windows.Shell;

/// <summary>
/// An icon in the notification area, on its own STA thread with its own message loop.
/// </summary>
/// <remarks>
/// <para>
/// <c>Shell_NotifyIcon</c> through P/Invoke rather than WinForms' <c>NotifyIcon</c>, so the engine
/// does not take a UI framework as a dependency for one icon and one menu.
/// </para>
/// <para>
/// It knows nothing about the engine. What it shows and what its menu does come from an
/// <see cref="ITrayModel"/>, read on this thread about once a second.
/// </para>
/// </remarks>
public sealed unsafe class TrayIcon : IDisposable
{
    const string ClassName = "VamEngineTray";
    const uint CallbackMessage = WmApp + 1;
    const uint TellMessage = WmApp + 2;
    const nuint RefreshTimer = 1;
    const uint RefreshMilliseconds = 1000;
    const int TipCharacters = 127;
    const int InfoCharacters = 255;
    const int InfoTitleCharacters = 63;
    const uint InfoIcon = 0x1;
    const int ClassAlreadyExists = 1410;

    static readonly TimeSpan StartWait = TimeSpan.FromSeconds(2);

    readonly ITrayModel model;
    readonly TrayIcons icons;
    readonly ILogger logger;
    readonly Thread thread;
    readonly ManualResetEventSlim started = new();
    readonly nint[] handles = new nint[3];

    volatile nint window;
    uint taskbarCreated;
    TrayView? shown;
    Tuple<string, string>? told;
    bool isClosing;
    bool isBusy;
    bool isDisposed;

    /// <summary>Prepares the icon. Nothing appears until <see cref="Start"/>.</summary>
    /// <param name="model">What to show and what the menu does.</param>
    /// <param name="icons">The artwork.</param>
    /// <param name="logger">Where a failure goes.</param>
    public TrayIcon(ITrayModel model, TrayIcons icons, ILogger logger)
    {
        this.model = model;
        this.icons = icons;
        this.logger = logger;

        thread = new Thread(Run)
        {
            Name = "VAM tray",
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>
    /// Whether this process can show an icon at all: false as a service, over SSH, or anywhere else
    /// without a desktop somebody is looking at.
    /// </summary>
    public static bool CanShow
    {
        get
        {
            using Process self = Process.GetCurrentProcess();

            return Environment.UserInteractive && self.SessionId != 0;
        }
    }

    /// <summary>Shows the icon.</summary>
    public void Start() => thread.Start();

    /// <summary>Shows a notification beside the icon. Any thread.</summary>
    /// <param name="title">Its heading.</param>
    /// <param name="text">What it says.</param>
    public void Tell(string title, string text)
    {
        Volatile.Write(ref told, Tuple.Create(title, text));

        if (window != 0)
        {
            PostMessage(window, TellMessage, 0, 0);
        }
    }

    /// <summary>Restores a window and brings it to the front.</summary>
    /// <param name="target">Its handle.</param>
    public static void BringToFront(nint target)
    {
        if (IsIconic(target))
        {
            ShowWindow(target, SwRestore);
        }

        SetForegroundWindow(target);
    }

    /// <summary>Removes the icon and ends its thread.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted))
        {
            started.Dispose();

            return;
        }

        started.Wait(StartWait);

        if (window != 0)
        {
            PostMessage(window, WmClose, 0, 0);
        }

        if (Thread.CurrentThread != thread)
        {
            thread.Join(StartWait);
        }

        started.Dispose();
    }

    [UnmanagedCallersOnly]
    static nint Procedure(nint target, uint message, nint wParam, nint lParam)
    {
        nint cookie = GetWindowLongPtr(target, GwlpUserData);

        if (cookie != 0 && GCHandle.FromIntPtr(cookie).Target is TrayIcon tray)
        {
            try
            {
                if (tray.Handle(message, lParam))
                {
                    return 0;
                }
            }
            catch (Exception failure)
            {
                // Nothing may unwind through a native frame, and the tray is not worth the engine.
                tray.logger.LogError(failure, "The tray icon failed to handle a message.");

                return 0;
            }
        }

        return DefWindowProc(target, message, wParam, lParam);
    }

    void Run()
    {
        GCHandle self = GCHandle.Alloc(this);

        try
        {
            window = CreateHiddenWindow();

            if (window == 0)
            {
                logger.LogWarning("The tray icon was not shown: Windows would not create its window ({Error}).", Marshal.GetLastPInvokeError());

                return;
            }

            SetWindowLongPtr(window, GwlpUserData, GCHandle.ToIntPtr(self));
            LoadIcons();
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            Refresh(isNew: true);
            SetTimer(window, RefreshTimer, RefreshMilliseconds, 0);
            started.Set();
            Pump();
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "The tray icon stopped.");
        }
        finally
        {
            started.Set();
            window = 0;

            foreach (nint handle in handles)
            {
                if (handle != 0)
                {
                    DestroyIcon(handle);
                }
            }

            self.Free();
        }
    }

    static nint CreateHiddenWindow()
    {
        nint instance = GetModuleHandle(0);

        fixed (char* name = ClassName)
        {
            WindowClass windowClass = new()
            {
                Size = (uint)sizeof(WindowClass),
                Procedure = &Procedure,
                Instance = instance,
                ClassName = name
            };

            if (RegisterClass(&windowClass) == 0 && Marshal.GetLastPInvokeError() != ClassAlreadyExists)
            {
                return 0;
            }
        }

        // A real top-level window, never shown. A message-only one cannot own the menu reliably.
        return CreateWindow(0, ClassName, ClassName, 0, 0, 0, 0, 0, 0, 0, instance, 0);
    }

    static void Pump()
    {
        Message message;

        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }

    void LoadIcons()
    {
        int size = GetSystemMetrics(SmCxSmIcon);

        handles[(int)TrayGlyph.Running] = IconFile.Load(icons.Running, size);
        handles[(int)TrayGlyph.Recording] = IconFile.Load(icons.Recording, size);
        handles[(int)TrayGlyph.Fault] = IconFile.Load(icons.Fault, size);
    }

    bool Handle(uint message, nint lParam)
    {
        switch (message)
        {
            case CallbackMessage:
                OnIcon((uint)(lParam & 0xFFFF));
                return true;

            case WmTimer:
                Refresh(isNew: false);
                return true;

            case TellMessage:
                ShowTold();
                return true;

            case WmClose:
                Close();
                return true;

            case WmDestroy:
                PostQuitMessage(0);
                return true;

            default:
                if (message == taskbarCreated && taskbarCreated != 0)
                {
                    // Explorer restarted and forgot every icon it had.
                    Refresh(isNew: true);

                    return true;
                }

                return false;
        }
    }

    void OnIcon(uint mouse)
    {
        // A menu or a question runs its own message loop, which would otherwise open a second one.
        if (isBusy)
        {
            return;
        }

        isBusy = true;

        try
        {
            if (mouse is WmRButtonUp or WmContextMenu)
            {
                ShowMenu();
            }
            else if (mouse == WmLButtonDblClk)
            {
                Act(Refresh(isNew: false).DefaultId);
            }
        }
        finally
        {
            isBusy = false;
        }
    }

    TrayView Refresh(bool isNew)
    {
        TrayView view = model.Read();

        if (!isNew && shown is { } previous && previous.Glyph == view.Glyph && previous.Tooltip == view.Tooltip)
        {
            return view;
        }

        NotifyIconData data = Describe(NifMessage | NifIcon | NifTip);

        data.CallbackMessage = CallbackMessage;
        data.Icon = handles[(int)view.Glyph];
        Copy(view.Tooltip, data.Tip, TipCharacters);

        // Modify fails when the icon is not there yet, which is the case while Explorer starts.
        if (!ShellNotifyIcon(isNew ? NimAdd : NimModify, &data) && !isNew)
        {
            ShellNotifyIcon(NimAdd, &data);
        }

        shown = view;

        return view;
    }

    void ShowMenu()
    {
        TrayView view = Refresh(isNew: false);
        nint menu = CreatePopupMenu();

        try
        {
            foreach (TrayMenuItem item in view.Menu)
            {
                AppendMenu(menu, item.Id == 0 ? MfSeparator : MfString, (nuint)item.Id, item.Id == 0 ? null : item.Text);
            }

            SetMenuDefaultItem(menu, (uint)view.DefaultId, 0);
            GetCursorPos(out ScreenPoint cursor);

            // Without the first the menu never closes when clicked away from; without the second it
            // sometimes closes the next time it opens. Both are documented Windows behaviour.
            SetForegroundWindow(window);
            int chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton, cursor.X, cursor.Y, window, 0);
            PostMessage(window, WmNull, 0, 0);

            Act(chosen);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    void Act(int id)
    {
        // Read again rather than trusting the menu: the state may have changed while it was open,
        // and an item the engine no longer offers, or a question it now needs, must not be skipped.
        TrayView view = model.Read();

        if (Offered(view, id) is not { } item)
        {
            return;
        }

        if (item.Confirmation is { } question
            && (MessageBox(window, question, view.Caption, MbYesNo | MbIconWarning | MbDefButton2 | MbSetForeground) != IdYes
                || Offered(model.Read(), id) is null))
        {
            return;
        }

        if (!isClosing)
        {
            model.Choose(id);
        }
    }

    static TrayMenuItem? Offered(TrayView view, int id) =>
        id == 0 ? null : view.Menu.FirstOrDefault(item => item.Id == id);

    void ShowTold()
    {
        if (Interlocked.Exchange(ref told, null) is not { } message)
        {
            return;
        }

        NotifyIconData data = Describe(NifInfo);

        data.InfoFlags = InfoIcon;
        Copy(message.Item1, data.InfoTitle, InfoTitleCharacters);
        Copy(message.Item2, data.Info, InfoCharacters);
        ShellNotifyIcon(NimModify, &data);
    }

    void Close()
    {
        isClosing = true;
        KillTimer(window, RefreshTimer);

        NotifyIconData data = Describe(0);

        ShellNotifyIcon(NimDelete, &data);
        DestroyWindow(window);
    }

    NotifyIconData Describe(uint flags) => new()
    {
        Size = (uint)sizeof(NotifyIconData),
        Window = window,
        Id = 1,
        Flags = flags
    };

    static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity);

        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity + 1));
        destination[length] = '\0';
    }
}
