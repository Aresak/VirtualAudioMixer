namespace Vam.Ui.Abstractions;

/// <summary>
/// Receives one decoded meter frame.
/// </summary>
/// <remarks>
/// A delegate rather than an event on a component, because meter frames must never reach the render
/// tree. Twenty-five frames a second through component diffing, across sixteen strips, is the single
/// most reliable way to make this console feel slow — and under a WebView it is how it stops
/// responding to a fader at all. The handler draws to a canvas and returns.
/// </remarks>
/// <param name="payload">The packed frame. Only valid for the duration of the call.</param>
/// <param name="channelCount">Strips in this frame.</param>
/// <param name="busCount">Buses in this frame.</param>
public delegate void MeterFrameHandler(ReadOnlySpan<byte> payload, int channelCount, int busCount);
