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
}
