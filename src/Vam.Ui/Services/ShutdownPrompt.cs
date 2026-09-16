namespace Vam.Ui.Services;

/// <summary>
/// The handshake between a host's window closing and the console's answer to it.
/// </summary>
/// <remarks>
/// <para>
/// A host cannot answer the question itself. "Should the engine stop too?" has to be asked in the
/// operator's language, in the console's own type and colours, and answered by the part of the
/// process that holds the session — none of which a native message box beside a web view has.
/// </para>
/// <para>
/// So the host cancels its close, says here that it was asked, and waits. The console puts the
/// question up, does whatever the answer was, and says here when the window may go.
/// </para>
/// <para>
/// A singleton, unlike the rest of the console's services. There is one window and one of these; a
/// scoped one would be a different instance from the one the host is holding, which is a handshake
/// with nobody on the other end.
/// </para>
/// </remarks>
public sealed class ShutdownPrompt
{
    /// <summary>Whether the question is up.</summary>
    public bool IsAsking { get; private set; }

    /// <summary>Raised when the host wants to close and the console should ask. Console listens.</summary>
    public event Action? Asked;

    /// <summary>Raised when the console is done and the window may close. Host listens.</summary>
    public event Action? Released;

    /// <summary>Whether anything is listening — a host that intercepts its close.</summary>
    /// <remarks>
    /// False in a browser, where there is no window to hold open and nothing to ask about.
    /// </remarks>
    public bool IsIntercepted => Released is not null;

    /// <summary>The host asks. Called from the host's close handler.</summary>
    public void Ask()
    {
        IsAsking = true;

        Asked?.Invoke();
    }

    /// <summary>The person changed their mind. The window stays.</summary>
    public void Dismiss()
    {
        IsAsking = false;

        Asked?.Invoke();
    }

    /// <summary>The console is finished. The window may go.</summary>
    public void Release()
    {
        IsAsking = false;

        Released?.Invoke();
    }
}
