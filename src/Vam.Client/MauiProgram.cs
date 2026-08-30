using Vam.Ui.Extensions;

namespace Vam.Client;

/// <summary>
/// The desktop client's startup, and the whole of its contribution.
/// </summary>
/// <remarks>
/// It registers the console, its platform services and a web view. Everything else the client does
/// lives in Vam.Ui, which is what stops the desktop and the browser drifting into two products with
/// two sets of bugs.
/// </remarks>
public static class MauiProgram
{
    /// <summary>Builds the app.</summary>
    /// <returns>It.</returns>
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

        builder.Services.AddMauiBlazorWebView();

        // Where the engine is. A setting even for the desktop client, because an operator running the
        // console on a laptop and the engine on the machine wired to the microphones is the normal
        // case rather than an advanced one.
        builder.Services.AddVamUi<DesktopPlatformServices>(options =>
            options.Address = Preferences.Default.Get("vam.engine", "http://localhost:5211"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
