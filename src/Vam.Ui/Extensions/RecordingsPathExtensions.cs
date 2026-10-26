using Vam.Protocol.V1;

namespace Vam.Ui.Extensions;

/// <summary>Turns the recording folder box into the command that moves the folder.</summary>
public static class RecordingsPathExtensions
{
    /// <summary>The command for what the box shows, or null when it shows nothing.</summary>
    /// <param name="shown">The path in the box. Surrounding space is ignored.</param>
    /// <returns>A move to that path, never a reset: only the Reset button resets.</returns>
    public static SetRecordingsPath? ToRecordingsPath(this string? shown)
    {
        string trimmed = shown?.Trim() ?? string.Empty;

        return trimmed.Length == 0 ? null : new SetRecordingsPath { Path = trimmed };
    }
}
