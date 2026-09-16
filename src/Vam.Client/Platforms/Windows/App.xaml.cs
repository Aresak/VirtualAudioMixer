using Microsoft.UI.Xaml;

namespace Vam.Client.WinUI;

/// <summary>The Windows entry point.</summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Builds it.</summary>
    public App() => InitializeComponent();

    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
