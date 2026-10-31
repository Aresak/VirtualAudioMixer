namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>One output's audio sessions, read one at a time. #133.</summary>
/// <remarks>
/// The seam <see cref="WasapiAudioSessionSnapshot"/> reads through, so that an output or a session
/// Windows will not describe can be tested without a sound card behind it.
/// </remarks>
public interface IAudioSessionOutput
{
    /// <summary>What the output is called, for the log.</summary>
    string Name { get; }

    /// <summary>Asks Windows for the output's sessions again.</summary>
    /// <returns>How many there are.</returns>
    /// <exception cref="System.Runtime.InteropServices.COMException">The output cannot be read at all.</exception>
    int Refresh();

    /// <summary>Reads one session.</summary>
    /// <param name="index">Which, from zero to what <see cref="Refresh"/> returned.</param>
    /// <returns>The session, or null for one that is not offered: system sounds, or expired.</returns>
    /// <exception cref="System.Runtime.InteropServices.COMException">This session cannot be read.</exception>
    AudioSessionEntry? Read(int index);
}
