using Windows.Storage;
using Windows.Storage.Pickers;
using Vam.Ui.Abstractions;
using WinRT.Interop;

namespace Vam.Client;

/// <summary>
/// What this host can do that the console cannot work out for itself.
/// </summary>
/// <remarks>
/// <para>
/// Almost nothing, and that is the design. The moment this class grows a method that is really a
/// feature, the feature has escaped into the hosts and will be written twice and fixed once.
/// </para>
/// <para>
/// It can open a folder picker, unlike the browser host — though only usefully when the engine is on
/// this machine, because the folder a recording goes into belongs to the engine's disk.
/// </para>
/// </remarks>
public sealed class DesktopPlatformServices(EngineLauncher launcher) : IPlatformServices
{
    const string RememberedEngineKey = "vam.engine";

    /// <inheritdoc />
    public string ClientName => "VAM Desktop Console";

    /// <inheritdoc />
    public bool CanPickFolders => true;

    /// <inheritdoc />
    public async ValueTask<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not
            Microsoft.UI.Xaml.Window window)
        {
            return null;
        }

        FolderPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = title
        };

        // Required on desktop: a WinUI picker has no window of its own and refuses to open without
        // being told which one it belongs to.
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        StorageFolder? folder = await picker.PickSingleFolderAsync().AsTask(cancellationToken);

        return folder?.Path;
    }

    /// <inheritdoc />
    /// <remarks>The engine ships beside this application, so this host can.</remarks>
    public bool CanStartEngine => true;

    /// <inheritdoc />
    public ValueTask<string?> StartEngineAsync(string address, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(launcher.Start(address));
    }

    /// <inheritdoc />
    public string? RememberedEngine
    {
        get => Preferences.Default.Get<string?>(RememberedEngineKey, null);
        set
        {
            if (value is null)
            {
                Preferences.Default.Remove(RememberedEngineKey);

                return;
            }

            Preferences.Default.Set(RememberedEngineKey, value);
        }
    }
}
