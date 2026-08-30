namespace Vam.Ui.State;

/// <summary>Which overlay is open over the mixer, if any. U3.</summary>
/// <remarks>
/// Over the mixer rather than away from it. An operator adjusting one channel has not stopped
/// needing to see the other fifteen, and a console that navigates to a settings page hides the
/// meeting behind the thing being adjusted.
/// </remarks>
public enum OverlayId
{
    /// <summary>The mixer, unobstructed.</summary>
    None,

    /// <summary>One channel: its chain, its trim, its automix weight.</summary>
    Channel,

    /// <summary>One bus: its role, its output, its mix-minus exclusions.</summary>
    Bus,

    /// <summary>U20's routing matrix, every strip against every bus.</summary>
    Matrix,

    /// <summary>Adding a strip or a bus. U17.</summary>
    Add
}
