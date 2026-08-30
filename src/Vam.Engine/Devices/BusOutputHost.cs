using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// Opens a render device for every bus that names one, and keeps them fed. D7 and E2.
/// </summary>
/// <remarks>
/// <para>
/// The mix thread writes a bus into a <see cref="BusOutputChannel"/>'s ring; the device's own thread
/// pulls from it. This is the piece that opens that thread. Without it a bus with an output device
/// configured is <b>silent</b> — the graph fills a ring nobody drains — which is a failure with no
/// symptom other than nothing coming out, and no error anywhere to explain it.
/// </para>
/// <para>
/// <b>The primary bus is not one of these.</b> It goes out through the master clock, because the
/// clock is the device whose callback drives the whole graph. Everything else is a follower with its
/// own clock, its own drift, and therefore its own rate-adapting ring — which is exactly what
/// <see cref="BusOutputChannel"/> is.
/// </para>
/// <para>
/// Control thread. Opening a device takes milliseconds and enumerates COM objects, and the audio
/// thread is the wrong one to do either on.
/// </para>
/// </remarks>
public sealed class BusOutputHost(IAudioBackend backend, ILoggerFactory loggers) : IDisposable
{
    readonly ILogger logger = loggers.CreateLogger<BusOutputHost>();
    readonly Dictionary<int, Binding> bindings = [];

    bool isDisposed;

    /// <summary>How many buses currently reach a device this way.</summary>
    public int Count => bindings.Count;

    /// <summary>The channel bound to one bus, or null.</summary>
    /// <param name="busIndex">Which bus.</param>
    /// <returns>Its channel, or null when that bus has no secondary output.</returns>
    public BusOutputChannel? ChannelOf(int busIndex) =>
        bindings.TryGetValue(busIndex, out Binding binding) ? binding.Channel : null;

    /// <summary>
    /// Opens or re-opens the render devices for a set of buses.
    /// </summary>
    /// <remarks>
    /// Called after every recompile, because a bus can be added, removed or re-aimed while the
    /// meeting runs. Buses already bound to the same device are left alone: closing and re-opening a
    /// working output because a different bus changed would put a gap in somebody's headphones.
    /// </remarks>
    /// <param name="wanted">Which bus should reach which device, and how wide.</param>
    /// <param name="options">How to size the rings.</param>
    public void Reconcile(IReadOnlyList<BusOutputRequest> wanted, DeviceInputChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(options);

        HashSet<int> keep = [];

        foreach (BusOutputRequest request in wanted)
        {
            if (request.DeviceId.IsNone)
            {
                continue;
            }

            keep.Add(request.BusIndex);

            if (bindings.TryGetValue(request.BusIndex, out Binding existing)
                && existing.Channel.DeviceId == request.DeviceId
                && existing.Channel.ChannelCount == request.ChannelCount)
            {
                continue;
            }

            Close(request.BusIndex);
            Open(request, options);
        }

        foreach (int busIndex in bindings.Keys.ToList())
        {
            if (!keep.Contains(busIndex))
            {
                Close(busIndex);
            }
        }
    }

    /// <summary>Advances every bound output's drift correction. Control thread.</summary>
    /// <param name="elapsed">Time since the last call.</param>
    public void UpdateCorrections(TimeSpan elapsed)
    {
        foreach (Binding binding in bindings.Values)
        {
            binding.Channel.UpdateCorrection(elapsed);
        }
    }

    /// <summary>Closes every output.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        foreach (int busIndex in bindings.Keys.ToList())
        {
            Close(busIndex);
        }
    }

    void Open(BusOutputRequest request, DeviceInputChannelOptions options)
    {
        try
        {
            BusOutputChannel channel = new(
                request.DeviceId,
                options with { ChannelCount = Math.Max(request.ChannelCount, 1) },
                loggers.CreateLogger<DeviceInputChannel>());

            IRenderStream stream = backend.OpenRender(
                request.DeviceId,
                new RenderOptions(ShareMode.Shared, options.CorrectionInterval));

            // The delegate is created once, here, and stored. Creating one per callback would
            // allocate inside the audio path.
            stream.Start(channel.Fill);

            bindings[request.BusIndex] = new Binding(channel, stream);

            logger.LogInformation(
                "Bus {Bus} is playing to {DeviceId}, {Channels} channels.",
                request.BusIndex,
                request.DeviceId.Value,
                request.ChannelCount);
        }
        catch (Exception failure)
        {
            // A monitor that will not open must not stop the meeting. The bus keeps mixing and
            // nobody hears it, which is what a headphone amplifier being unplugged looks like
            // anyway — and the line below is how an operator finds out why.
            logger.LogError(
                failure,
                "Bus {Bus} could not open {DeviceId}. It will keep mixing, and nothing will come out of it.",
                request.BusIndex,
                request.DeviceId.Value);
        }
    }

    void Close(int busIndex)
    {
        if (!bindings.Remove(busIndex, out Binding binding))
        {
            return;
        }

        binding.Stream.Dispose();
        binding.Channel.Reset();
    }

    readonly record struct Binding(BusOutputChannel Channel, IRenderStream Stream);
}
