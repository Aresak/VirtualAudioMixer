namespace Vam.Server.Engine;

/// <summary>What a recording session writes. E3.</summary>
/// <remarks>
/// Four decisions rather than four arguments, because they are only ever set together and a session
/// already running cannot honour any of them: tracks cannot be added to files that are open.
/// </remarks>
public sealed record CaptureSelection
{
    /// <summary>The engine's only format today. 24-bit WAV at whatever rate the engine runs.</summary>
    public const string WaveFormat = "wav24";

    /// <summary>Every input, before any processing. The record a public body actually needs.</summary>
    public bool Inputs { get; init; } = true;

    /// <summary>The primary bus, finished, as it went out.</summary>
    public bool StreamBus { get; init; } = true;

    /// <summary>Every other bus as well. Off, because it multiplies the session size.</summary>
    public bool AllBuses { get; init; }

    /// <summary>Which of the engine's formats to write.</summary>
    public string Format { get; init; } = WaveFormat;
}
