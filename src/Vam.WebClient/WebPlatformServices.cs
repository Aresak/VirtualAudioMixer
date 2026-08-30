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

    /// <inheritdoc />
    /// <remarks>
    /// It could start a process, and it must not. This host runs wherever the page is served from,
    /// which is not necessarily — and for a console reached over the network, not usually — the
    /// machine with the microphones in it. An engine started here would be an engine with no inputs,
    /// and every browser that loaded the page could start another.
    /// </remarks>
    public bool CanStartEngine => false;

    /// <inheritdoc />
    public ValueTask<string?> StartEngineAsync(string address, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>("A web console cannot start an engine. Start one on the machine with the microphones.");

    /// <inheritdoc />
    /// <remarks>
    /// This host is configured rather than asked, and one process serves every browser: a remembered
    /// address here would be one operator's choice arriving in somebody else's console.
    /// </remarks>
    public string? RememberedEngine
    {
        get => null;
        set { }
    }
}
