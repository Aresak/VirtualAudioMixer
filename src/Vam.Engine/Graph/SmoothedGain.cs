namespace Vam.Engine.Graph;

/// <summary>
/// A gain that slides towards its target instead of jumping to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Advanced once per block, and constant within it.</b> At 2.5 ms a block-constant gain is
/// inaudible for speech, and it keeps per-sample parameter interpolation out of every kernel in the
/// engine — which is the difference between smoothing being free and smoothing being the most
/// expensive thing in the mix.
/// </para>
/// <para>
/// This is why the epic can claim a fader move applies mid-stream with no discontinuity. Without it
/// a snapshot swap steps the gain between one block and the next, and a step in gain is a click.
/// </para>
/// <para>
/// A struct, held as node state. Inside the audio path; it is two multiplies and an add.
/// </para>
/// </remarks>
public struct SmoothedGain
{
    /// <summary>Below this the target is close enough to have arrived.</summary>
    /// <remarks>
    /// A one-pole never quite reaches its target. Snapping when the remaining difference is far
    /// below anything audible is what stops a muted strip from multiplying by a denormal forever.
    /// </remarks>
    const float ArrivedThreshold = 1e-7f;

    float current;

    /// <summary>Starts at a value, already settled there.</summary>
    /// <param name="initial">The starting gain.</param>
    public SmoothedGain(float initial) => current = initial;

    /// <summary>The gain to use for this block.</summary>
    public readonly float Value => current;

    /// <summary>
    /// Moves one block closer to a target.
    /// </summary>
    /// <param name="target">Where the gain is heading.</param>
    /// <param name="coefficient">How far to move this block, from the plan's smoothing time.</param>
    /// <returns>The gain to use for this block.</returns>
    public float Advance(float target, float coefficient)
    {
        float difference = target - current;

        current = Math.Abs(difference) < ArrivedThreshold
            ? target
            : current + (difference * coefficient);

        return current;
    }

    /// <summary>Jumps to a value without sliding. For a plan being installed, never mid-session.</summary>
    /// <param name="value">The gain.</param>
    public void Reset(float value) => current = value;
}
