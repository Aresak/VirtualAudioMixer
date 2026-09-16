namespace Vam.Engine.Devices.Abstractions;

/// <summary>
/// Hands captured audio to whoever asked for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This runs inside the audio path.</b> It must not allocate, lock or wait. See
/// <c>docs/audio-path.md</c>. In practice the only thing an implementation should do here is copy
/// into a ring buffer and return.
/// </para>
/// <para>
/// A span rather than an array, so no buffer is allocated per callback and nothing can retain the
/// samples past the call. A named delegate rather than <c>Action</c> because a span cannot be a
/// generic type argument - which is a feature here, not a limitation.
/// </para>
/// </remarks>
/// <param name="samples">
/// Interleaved samples, <paramref name="frameCount"/> frames of the stream's channel count. Valid
/// only for the duration of the call.
/// </param>
/// <param name="frameCount">Frames in this buffer.</param>
public delegate void CaptureCallback(ReadOnlySpan<float> samples, int frameCount);
