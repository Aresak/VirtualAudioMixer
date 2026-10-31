namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>Lists the audio sessions open on this machine's outputs.</summary>
public interface IAudioSessionSnapshot
{
    /// <summary>Takes a snapshot.</summary>
    /// <returns>Every session that has not expired, system sounds left out.</returns>
    IReadOnlyList<AudioSessionEntry> Take();
}
