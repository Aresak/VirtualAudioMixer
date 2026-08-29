namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// Asks for audio to play. A pull model: the device asks, rather than being pushed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This runs inside the audio path.</b> It must not allocate, lock or wait. See
/// <c>docs/audio-path.md</c>.
/// </para>
/// <para>
/// The pull shape is what the master clock later hangs on - one designated render device's
/// callback drives the whole graph, and everything else resamples to it. Filling fewer frames than
/// asked is allowed and is not an error: the remainder is played as silence and counted as an
/// underrun. Blocking to avoid that would turn a click into a stall.
/// </para>
/// </remarks>
/// <param name="destination">
/// Buffer to fill with interleaved samples. Valid only for the duration of the call.
/// </param>
/// <param name="frameCount">Frames wanted.</param>
/// <returns>Frames actually written. Anything short of <paramref name="frameCount"/> plays as silence.</returns>
public delegate int RenderCallback(Span<float> destination, int frameCount);
