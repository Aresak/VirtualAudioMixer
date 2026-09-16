using Vam.Ui.Services;

namespace Vam.Client;

/// <summary>The application.</summary>
public partial class App : Application
{
    /// <summary>Builds it.</summary>
    public App() => InitializeComponent();

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        Window window = new(new MainPage())
        {
            Title = "VAM",

            // Sixteen strips at a hundred and twenty-eight pixels each, plus the rail and the buses.
            // A console that opens too small to see the console is a console somebody resizes before
            // every meeting.
            MinimumWidth = 1100,
            MinimumHeight = 700
        };

        window.Created += (_, _) => Intercept(window);

        return window;
    }

    /// <summary>
    /// Holds the window open long enough for the console to ask whether the engine should stop too.
    /// </summary>
    /// <remarks>
    /// The question is not asked here. It belongs in the console: in the operator's language, in the
    /// console's own type and colours, answered by the part of the process that holds the session. A
    /// native message box beside a web view has none of those.
    /// </remarks>
    static void Intercept(Window window)
    {
#if WINDOWS
        if (IPlatformApplication.Current?.Services.GetService<ShutdownPrompt>() is not { } prompt)
        {
            return;
        }

        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
        {
            return;
        }

        bool released = false;

        prompt.Released += () => window.Dispatcher.Dispatch(() =>
        {
            released = true;

            native.Close();
        });

        native.AppWindow.Closing += (_, args) =>
        {
            if (released)
            {
                return;
            }

            // Cancelled before anything is awaited. The argument is only read while the handler is on
            // the stack, so a close that is decided asynchronously has to be refused first and
            // repeated afterwards.
            args.Cancel = true;

            prompt.Ask();
        };
#endif
    }
}
