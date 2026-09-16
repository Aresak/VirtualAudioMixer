namespace Vam.TestKit.Devices;

/// <summary>What a <see cref="NullAudioBackend"/> capture device produces.</summary>
public enum NullSignal
{
    /// <summary>Digital silence.</summary>
    Silence,

    /// <summary>A continuous sine at the configured frequency, phase-continuous across buffers.</summary>
    Tone,

    /// <summary>
    /// A counter incrementing by one per frame, written to every channel. Ugly to listen to and
    /// ideal for proving that no frame was lost, duplicated or reordered.
    /// </summary>
    Ramp
}
