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

        // Where the engine is is not decided here. The console looks for one, and asks only if it
        // finds none: an operator running the console on a laptop and the engine on the machine
        // wired to the microphones is a normal case rather than an advanced one, but so is both on
        // one machine, and only one of those should involve a question.
        builder.Services.AddSingleton<EngineLauncher>();
        builder.Services.AddVamUi<DesktopPlatformServices>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
