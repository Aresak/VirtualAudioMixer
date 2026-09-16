using System.Globalization;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Which device channels feed which strips.
/// </summary>
/// <remarks>
/// <para>
/// A stereo interface is two mono strips if the operator wants it to be, so a device channel has to
/// be addressable independently of its device. This is where that decision lives.
/// </para>
/// <para>
/// <b>Validated before anything opens, never in the audio path.</b> The de-interleaver trusts the
/// map completely — it does no bounds checking of its own — and that trust is only earned because
/// <see cref="Validate"/> ran first and refused anything that would have gone out of range.
/// </para>
/// <para>
/// Control thread only. It allocates freely.
/// </para>
/// </remarks>
public sealed class ChannelMap
{
    readonly List<ChannelSource> sources = [];

    /// <summary>The mapping, in the order the strips were added.</summary>
    public IReadOnlyList<ChannelSource> Sources => sources;

    /// <summary>Adds a source.</summary>
    /// <param name="source">Which device channels feed which strip.</param>
    public void Add(ChannelSource source) => sources.Add(source);

    /// <summary>Removes every source feeding a strip.</summary>
    /// <param name="stripIndex">Which strip.</param>
    /// <returns>How many were removed.</returns>
    public int RemoveStrip(int stripIndex) => sources.RemoveAll(source => source.StripIndex == stripIndex);

    /// <summary>Forgets everything.</summary>
    public void Clear() => sources.Clear();

    /// <summary>
    /// Checks the map against the devices that are actually present.
    /// </summary>
    /// <remarks>
    /// Returns every problem rather than the first. An operator fixing a console at five to seven
    /// should see all of it at once, not discover the next fault after each repair.
    /// </remarks>
    /// <param name="present">The devices available now.</param>
    /// <returns>What is wrong. Empty means the map is safe to open streams against.</returns>
    public IReadOnlyList<ChannelMapProblem> Validate(IReadOnlyList<AudioDeviceInfo> present)
    {
        ArgumentNullException.ThrowIfNull(present);

        List<ChannelMapProblem> problems = [];
        Dictionary<int, ChannelSource> claimedStrips = [];

        foreach (ChannelSource source in sources)
        {
            CheckShape(source, problems);
            CheckDevice(source, present, problems);
            CheckStrip(source, claimedStrips, problems);
        }

        return problems;
    }

    static void CheckShape(ChannelSource source, List<ChannelMapProblem> problems)
    {
        if (source.ChannelCount >= 1 && source.FirstChannel >= 0 && source.StripIndex >= 0)
        {
            return;
        }

        problems.Add(new ChannelMapProblem(
            ChannelMapProblemKind.MalformedSource,
            source,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Strip {source.StripIndex} asks for {source.ChannelCount} channels from index "
                + $"{source.FirstChannel}, which is not a run of channels.")));
    }

    static void CheckDevice(ChannelSource source, IReadOnlyList<AudioDeviceInfo> present, List<ChannelMapProblem> problems)
    {
        AudioDeviceInfo? device = null;

        foreach (AudioDeviceInfo candidate in present)
        {
            if (candidate.Id == source.DeviceId)
            {
                device = candidate;
                break;
            }
        }

        if (device is null)
        {
            problems.Add(new ChannelMapProblem(
                ChannelMapProblemKind.DeviceAbsent,
                source,
                $"Strip {source.StripIndex} expects device '{source.DeviceId}', which is not present."));

            return;
        }

        if (source.ChannelCount >= 1 && source.FirstChannel >= 0 && source.ChannelLimit > device.ChannelCount)
        {
            problems.Add(new ChannelMapProblem(
                ChannelMapProblemKind.ChannelOutOfRange,
                source,
                $"Strip {source.StripIndex} reads channels {source.FirstChannel}-{source.ChannelLimit - 1} of "
                + $"'{device.FriendlyName}', which offers {device.ChannelCount}."));
        }
    }

    static void CheckStrip(
        ChannelSource source,
        Dictionary<int, ChannelSource> claimedStrips,
        List<ChannelMapProblem> problems)
    {
        if (claimedStrips.TryGetValue(source.StripIndex, out ChannelSource existing))
        {
            problems.Add(new ChannelMapProblem(
                ChannelMapProblemKind.StripClaimedTwice,
                source,
                $"Strip {source.StripIndex} is claimed by '{existing.DeviceId}' and by '{source.DeviceId}'."));

            return;
        }

        claimedStrips[source.StripIndex] = source;
    }
}
