namespace Vam.Engine.Recording;

/// <summary>What a recorded track is a recording of.</summary>
public enum RecordingSourceKind
{
    /// <summary>An input, tapped before the fader and before the automixer.</summary>
    Channel,

    /// <summary>A bus, tapped finished — after everything that shapes it.</summary>
    Bus
}
