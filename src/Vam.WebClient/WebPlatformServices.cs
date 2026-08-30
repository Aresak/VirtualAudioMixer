using Vam.Ui.Abstractions;

namespace Vam.WebClient;

/// <summary>
/// What this host can do that the console cannot work out for itself.
/// </summary>
/// <remarks>
/// Almost nothing, which is the point. A browser cannot open a native folder picker, and the folder
/// that matters is on the engine's machine rather than on the one running the browser — so the
/// recording view asks first and shows a typed path instead of a button that would do nothing.
/// </remarks>
public sealed class WebPlatformServices : IPlatformServices
{
    /// <inheritdoc />
    public string ClientName => "VAM Web Console";

    /// <inheritdoc />
    public bool CanPickFolders => false;

    /// <inheritdoc />
    public ValueTask<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}
