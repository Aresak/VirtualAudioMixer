namespace Vam.Ui.Abstractions;

/// <summary>
/// The few things a console cannot do without knowing what it is running inside.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny. Every view, view model and service in the product lives in <c>Vam.Ui</c>; a
/// host contributes its startup file and one implementation of this. The moment this interface grows
/// a method that is really a feature, that feature has escaped into the hosts and will be written
/// twice and fixed once.
/// </para>
/// <para>
/// Nothing here is on a hot path. A file picker takes as long as a person takes.
/// </para>
/// </remarks>
public interface IPlatformServices
{
    /// <summary>What to call this console when it introduces itself to an engine.</summary>
    string ClientName { get; }

    /// <summary>Whether this host can put up a native folder picker.</summary>
    /// <remarks>
    /// A browser cannot. The recording view asks first and shows a typed path instead of a button
    /// that would do nothing — U8's rule that the console never pretends to have done something.
    /// </remarks>
    bool CanPickFolders { get; }

    /// <summary>Asks for a folder, or returns null if the person changed their mind.</summary>
    /// <param name="title">What the dialog should say it is for.</param>
    /// <param name="cancellationToken">Abandons the wait, not the dialog.</param>
    /// <returns>The chosen path, or null.</returns>
    ValueTask<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Whether this host can start an engine on the machine it is running on.</summary>
    /// <remarks>
    /// A desktop console can: the engine ships beside it and starting a process is what a desktop
    /// application is allowed to do. A browser cannot, and neither can the process serving it —
    /// spawning an engine on a web host would put it on whichever machine happens to serve the page,
    /// which is not the machine with the microphones in it.
    /// </remarks>
    bool CanStartEngine { get; }

    /// <summary>Launches an engine on this machine.</summary>
    /// <remarks>
    /// <para>
    /// Returns once the process has been started, not once it is answering. Waiting for an answer is
    /// the console's job and it does it by asking, which keeps the one thing a host knows how to do
    /// — start a process — separate from the one thing it should not have to reimplement.
    /// </para>
    /// <para>
    /// The engine outlives the console that started it. G1 has the session surviving a console being
    /// closed, crashed or killed by somebody who thought it had hung, and an engine that went down
    /// with the window that launched it would make that untrue for the ordinary case.
    /// </para>
    /// </remarks>
    /// <param name="address">Where the engine should listen.</param>
    /// <param name="cancellationToken">Gives up before launching.</param>
    /// <returns>Null when the process started; otherwise a sentence saying what stopped it.</returns>
    ValueTask<string?> StartEngineAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// The engine address this host was last told to use, and where to put a new one.
    /// </summary>
    /// <remarks>
    /// Null means this host does not remember, which is a real answer and not a failure: a web host
    /// is configured rather than asked, and a per-browser memory on a process shared by every
    /// browser would be one operator's address arriving in somebody else's console.
    /// </remarks>
    string? RememberedEngine { get; set; }
}
