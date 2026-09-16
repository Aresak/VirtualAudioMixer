namespace Vam.Engine.Devices.Abstractions;

/// <summary>An open render device.</summary>
public interface IRenderStream : IAudioStream
{
    /// <summary>
    /// Begins rendering, pulling buffers from <paramref name="onBufferNeeded"/>.
    /// </summary>
    /// <remarks>
    /// The callback is taken here rather than set as a property so a stream cannot be running with
    /// nothing to play. Store it once - creating a delegate per callback would allocate inside the
    /// audio path.
    /// </remarks>
    /// <param name="onBufferNeeded">Fills each buffer. Runs inside the audio path.</param>
    void Start(RenderCallback onBufferNeeded);

    /// <summary>
    /// Buffers the callback did not fill completely, counted since the stream started. Monotonic.
    /// </summary>
    long UnderrunCount { get; }
}
