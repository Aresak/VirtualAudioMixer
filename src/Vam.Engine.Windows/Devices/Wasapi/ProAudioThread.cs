using System.Runtime.InteropServices;

namespace Vam.Engine.Windows.Devices.Wasapi;

/// <summary>
/// Registers the calling thread with the Multimedia Class Scheduler for the lifetime of this object.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an optimisation.</b> Without it Windows will preempt an audio thread for the UI renderer
/// like any other, and the symptom is the most-reported bug in software of this kind: it glitches
/// when you move the mouse. MMCSS is the supported way to say "this thread has a deadline" without
/// asking for real-time priority, which a user-mode application should not have.
/// </para>
/// <para>
/// Failure is not fatal and is deliberately not thrown. A machine with the MMCSS service disabled
/// still runs audio, just with worse jitter, and refusing to open a device over it would turn a
/// degraded session into no session. <see cref="IsRegistered"/> says which happened so a caller can
/// report it.
/// </para>
/// </remarks>
public sealed partial class ProAudioThread : IDisposable
{
    /// <summary>
    /// The MMCSS task name for audio with a deadline. One of a fixed set Windows defines in the
    /// registry under <c>SystemProfile\Tasks</c>; an unknown name simply fails to register.
    /// </summary>
    const string TaskName = "Pro Audio";

    readonly nint handle;

    /// <summary>Registers the calling thread. Construct it on the thread it is meant to cover.</summary>
    public ProAudioThread()
    {
        int taskIndex = 0;
        handle = AvSetMmThreadCharacteristics(TaskName, ref taskIndex);

        if (handle == 0)
        {
            LastError = Marshal.GetLastPInvokeError();
        }
    }

    /// <summary>Whether the thread is actually registered. False means the audio still runs, with worse jitter.</summary>
    public bool IsRegistered => handle != 0;

    /// <summary>The Win32 error when registration failed, or zero.</summary>
    public int LastError { get; }

    /// <summary>Unregisters the thread.</summary>
    public void Dispose()
    {
        if (handle != 0)
        {
            AvRevertMmThreadCharacteristics(handle);
        }
    }

    // These two carry an explicit `private` against the house rule that omits it. A partial method
    // with a return type must state its accessibility - CS8796 - so this is the language talking
    // rather than a preference.
    [LibraryImport(
        "avrt.dll",
        EntryPoint = "AvSetMmThreadCharacteristicsW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true
    )]
    private static partial nint AvSetMmThreadCharacteristics(string taskName, ref int taskIndex);

    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AvRevertMmThreadCharacteristics(nint handle);
}
