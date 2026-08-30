namespace Vam.Client;

/// <summary>The application.</summary>
public partial class App : Application
{
    /// <summary>Builds it.</summary>
    public App() => InitializeComponent();

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage())
        {
            Title = "VAM",

            // Sixteen strips at a hundred and twenty-eight pixels each, plus the rail and the buses.
            // A console that opens too small to see the console is a console somebody resizes before
            // every meeting.
            MinimumWidth = 1100,
            MinimumHeight = 700
        };
}
