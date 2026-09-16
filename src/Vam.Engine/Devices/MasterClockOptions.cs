namespace Vam.Engine.Devices;

/// <summary>
/// How the master clock is sized. Everything it will ever need is allocated from these numbers.
/// </summary>
/// <remarks>
/// Limits rather than current counts, because the arena is allocated once and devices come and go
/// during a session. Sizing it to what is plugged in at startup would mean reallocating on the
/// control thread while the audio thread reads it, which is the one thing the arena exists to avoid.
/// </remarks>
public sealed record MasterClockOptions
{
    /// <summary>Frames per block. The graph's quantum, and the clock's tick.</summary>
    public required int BlockFrames { get; init; }

    /// <summary>The rate the engine runs at. Used by the timer fallback to keep time on its own.</summary>
    public required int SampleRate { get; init; }

    /// <summary>Most input devices the session will ever have open at once.</summary>
    public required int MaxDevices { get; init; }

    /// <summary>Most channels any one device will present.</summary>
    public required int MaxChannelsPerDevice { get; init; }
}
