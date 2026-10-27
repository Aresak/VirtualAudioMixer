using Vam.Protocol.V1;
using Vam.Ui.Localization;

namespace Vam.Ui.Extensions;

/// <summary>Reads the programs with audio the engine reported, for a strip's source selector. #95.</summary>
public static class AudioProcessExtensions
{
    /// <summary>The programs not already offered as a device.</summary>
    /// <remarks>
    /// A program a strip already uses is listed by <c>ListDevices</c> too, and offering it twice
    /// would be two options for one source.
    /// </remarks>
    /// <param name="processes">The programs.</param>
    /// <param name="devices">The capture devices offered beside them.</param>
    /// <returns>The programs to offer.</returns>
    public static IReadOnlyList<AudioProcess> Besides(this IEnumerable<AudioProcess> processes, IEnumerable<DeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(devices);

        HashSet<string> offered = devices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. processes.Where(process => !offered.Contains(process.Id))];
    }

    /// <summary>The name of the program with this ID, for a selector's hover text.</summary>
    /// <param name="processes">The programs to look in.</param>
    /// <param name="deviceId">The source's ID. Null or empty finds nothing.</param>
    /// <returns>The program's name, or an empty string when no program has that ID.</returns>
    public static string NameOf(this IEnumerable<AudioProcess> processes, string? deviceId)
    {
        ArgumentNullException.ThrowIfNull(processes);

        return processes.FirstOrDefault(process => process.Id == deviceId)?.Name ?? string.Empty;
    }

    /// <summary>What a selector shows for the program.</summary>
    /// <param name="process">The program.</param>
    /// <param name="localizer">The console's words.</param>
    /// <returns>Its name, marked when it is playing now.</returns>
    public static string ToOptionLabel(this AudioProcess process, VamLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(localizer);

        return process.IsPlaying ? localizer.Format("strip.programPlaying", process.Name) : process.Name;
    }
}
