using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>The audio sessions on every active output, from WASAPI's session manager.</summary>
/// <remarks>Asked when a console opens a selector; never the audio path.</remarks>
public sealed class WasapiAudioSessionSnapshot : IAudioSessionSnapshot
{
    /// <inheritdoc />
    public IReadOnlyList<AudioSessionEntry> Take()
    {
        List<AudioSessionEntry> sessions = [];
        using MMDeviceEnumerator enumerator = new();

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    Collect(device, sessions);
                }
                catch (COMException)
                {
                    // An output unplugged while being read has no sessions worth offering.
                }
            }
        }

        return sessions;
    }

    static void Collect(MMDevice device, List<AudioSessionEntry> sessions)
    {
        AudioSessionManager manager = device.AudioSessionManager;
        manager.RefreshSessions();
        SessionCollection collection = manager.Sessions;

        for (int index = 0; index < collection.Count; index++)
        {
            using AudioSessionControl session = collection[index];
            AudioSessionState state = session.State;

            if (session.IsSystemSoundsSession || state == AudioSessionState.AudioSessionStateExpired)
            {
                continue;
            }

            sessions.Add(new AudioSessionEntry((int)session.GetProcessID, state == AudioSessionState.AudioSessionStateActive));
        }
    }
}
