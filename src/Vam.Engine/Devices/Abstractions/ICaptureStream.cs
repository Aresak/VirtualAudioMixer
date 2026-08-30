namespace Vam.Engine.Devices.Abstractions;

/// <summary>An open capture device.</summary>
public interface ICaptureStream : IAudioStream
{
    /// <summary>
    /// Begins capturing, delivering buffers to <paramref name="onSamplesCaptured"/>.
    /// </summary>
    /// <remarks>
    /// The callback is taken here rather than set as a property so a stream cannot be running
    /// without somewhere to put the audio. Store it once - creating a delegate per callback would
    /// allocate inside the audio path.
    /// </remarks>
    /// <param name="onSamplesCaptured">Receives each buffer. Runs inside the audio path.</param>
    void Start(CaptureCallback onSamplesCaptured);
}
