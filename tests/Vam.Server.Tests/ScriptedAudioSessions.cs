using Vam.Engine.Windows.Devices.ProcessLoopback;

namespace Vam.Server.Tests;

/// <summary>The audio sessions a test writes, so a browser playing is one line.</summary>
sealed class ScriptedAudioSessions : IAudioSessionSnapshot
{
    readonly List<AudioSessionEntry> open = [];

    public void Open(int processId, bool isActive) => open.Add(new AudioSessionEntry(processId, isActive));

    public IReadOnlyList<AudioSessionEntry> Take() => [.. open];
}
