namespace Vam.Ui.State;

/// <summary>How a meter moves. F1.</summary>
/// <remarks>
/// A console-side choice, and deliberately so: the engine sends peak and average every frame and
/// knows nothing about how they are drawn. Changing this costs one canvas and no protocol.
/// </remarks>
public enum MeterBallistics
{
    /// <summary>Peak-led, rising instantly and falling slowly. What to watch for an overshoot.</summary>
    Ppm,

    /// <summary>Average-led, with the peak drawn as a line above it. How loud it sounded.</summary>
    Rms,

    /// <summary>Average-led and slow, the way a needle moves. Easiest to read across a room.</summary>
    Vu
}
