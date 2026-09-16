namespace Vam.Ui.State;

/// <summary>Which phase a rail destination belongs to. U2.</summary>
public enum RailPhase
{
    /// <summary>In this build, and reachable.</summary>
    Mvp,

    /// <summary>Phase 1. It has a place in the rail before it has code, so it cannot be bolted on later.</summary>
    Phase1,

    /// <summary>Later than Phase 1.</summary>
    Later
}
