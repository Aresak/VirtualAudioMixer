using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Vam.Engine.Windows.Devices.ProcessLoopback;

/// <summary>The audio sessions on every active output, from WASAPI's session manager.</summary>
/// <remarks>Asked when a console opens a selector; never the audio path.</remarks>
/// <param name="logger">Where an output or a session that could not be read is reported.</param>
public sealed class WasapiAudioSessionSnapshot(ILogger logger) : IAudioSessionSnapshot
{
    /// <inheritdoc />
    public IReadOnlyList<AudioSessionEntry> Take()
    {
        using MMDeviceEnumerator enumerator = new();
        List<WasapiOutput> outputs = [];

        try
        {
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                outputs.Add(new WasapiOutput(device));
            }

            return Collect(outputs, logger);
        }
        finally
        {
            foreach (WasapiOutput output in outputs)
            {
                output.Dispose();
            }
        }
    }

    /// <summary>Reads every output's sessions, skipping what cannot be read rather than everything beside it.</summary>
    /// <param name="outputs">The outputs.</param>
    /// <param name="logger">Where a skip is reported.</param>
    /// <returns>The sessions that could be read.</returns>
    /// <remarks>
    /// One bad session used to take every session on its output with it, without a word, and the
    /// browser the operator was looking for was simply not in "Programs with audio". Now the others
    /// are still listed, and the log says what was skipped: one line per output, so a selector opened
    /// twenty times is not twenty lines for every broken session. #133.
    /// </remarks>
    public static IReadOnlyList<AudioSessionEntry> Collect(IEnumerable<IAudioSessionOutput> outputs, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(logger);

        List<AudioSessionEntry> sessions = [];

        foreach (IAudioSessionOutput output in outputs)
        {
            int count;

            try
            {
                count = output.Refresh();
            }
            catch (COMException failure)
            {
                // Most often an output unplugged while being read, which has no sessions worth offering.
                logger.LogWarning(
                    "The audio sessions on {Output} could not be read (0x{Result:X8}), so none of its programs are offered.",
                    output.Name,
                    failure.HResult);

                continue;
            }

            int skipped = 0;
            int result = 0;

            for (int index = 0; index < count; index++)
            {
                try
                {
                    if (output.Read(index) is { } session)
                    {
                        sessions.Add(session);
                    }
                }
                catch (COMException failure)
                {
                    skipped++;
                    result = failure.HResult;
                }
            }

            if (skipped > 0)
            {
                logger.LogWarning(
                    "{Skipped} of the {Count} audio sessions on {Output} could not be read (0x{Result:X8}) and are not offered.",
                    skipped,
                    count,
                    output.Name,
                    result);
            }
        }

        return sessions;
    }

    sealed class WasapiOutput(MMDevice device) : IAudioSessionOutput, IDisposable
    {
        SessionCollection? collection;

        public string Name
        {
            get
            {
                // Read only when something has already gone wrong, so it must not throw in turn: an
                // output going away mid-read may not answer to either.
                try
                {
                    return device.FriendlyName;
                }
                catch (COMException)
                {
                }

                try
                {
                    return device.ID;
                }
                catch (COMException)
                {
                    return "an output";
                }
            }
        }

        public int Refresh()
        {
            AudioSessionManager manager = device.AudioSessionManager;
            manager.RefreshSessions();
            collection = manager.Sessions;

            return collection.Count;
        }

        public AudioSessionEntry? Read(int index)
        {
            using AudioSessionControl session = collection![index];
            AudioSessionState state = session.State;

            if (session.IsSystemSoundsSession || state == AudioSessionState.AudioSessionStateExpired)
            {
                return null;
            }

            return new AudioSessionEntry((int)session.GetProcessID, state == AudioSessionState.AudioSessionStateActive);
        }

        public void Dispose() => device.Dispose();
    }
}
