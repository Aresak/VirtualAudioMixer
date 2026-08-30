namespace Vam.Ui.State;

/// <summary>Which view the rail is on. U2.</summary>
public enum ViewId
{
    /// <summary>The strips. Where a meeting is actually run from.</summary>
    Mixer,

    /// <summary>The automixer's settings and what it is currently doing.</summary>
    Automix,

    /// <summary>D5 monitor buses.</summary>
    Monitors,

    /// <summary>Recording, its destination and what the disk has left.</summary>
    Recording,

    /// <summary>K1 to K7. Clock, drift, dropouts, callbacks, allocations, cost, log.</summary>
    Diagnostics,

    /// <summary>Language, density, engine address, licences.</summary>
    Settings
}
