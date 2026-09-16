using Microsoft.Extensions.Logging;
using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// A bus on its way to a device that is not the one keeping time. D7.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the same problem as an input, with the arrows reversed,</b> so it is the same
/// solution: something writes at one rate, something else reads at another, and a servo holds the
/// ring between them by trimming a resampler. The mix thread writes at the master clock's rate; the
/// device thread reads at whatever its own crystal is actually doing.
/// </para>
/// <para>
/// It therefore <i>contains</i> a <see cref="DeviceInputChannel"/> rather than reimplementing one.
/// The names read backwards inside — a bus writing and a device pulling — which is exactly why the
/// wrapper exists: the mechanism is shared, the vocabulary is not.
/// </para>
/// <para>
/// The primary output does not need any of this. It is the clock, so the mix thread <i>is</i> its
/// device thread and there is nothing to adapt between. Only the second and later outputs come
/// through here.
/// </para>
/// </remarks>
public sealed class BusOutputChannel
{
    readonly DeviceInputChannel channel;

    /// <summary>Builds the adaptation between the master clock and one output device.</summary>
    /// <param name="deviceId">The device this bus feeds.</param>
    /// <param name="options">Rates, sizes and the fill setpoint.</param>
    /// <param name="logger">Where a clamped correction is reported.</param>
    public BusOutputChannel(
        AudioDeviceId deviceId,
        DeviceInputChannelOptions options,
        ILogger<DeviceInputChannel> logger)
    {
        channel = new DeviceInputChannel(deviceId, options, logger);
    }

    /// <summary>The device this bus feeds.</summary>
    public AudioDeviceId DeviceId => channel.DeviceId;

    /// <summary>Channels the bus carries.</summary>
    public int ChannelCount => channel.ChannelCount;

    /// <summary>Frames waiting to be played.</summary>
    public int FillFrames => channel.FillFrames;

    /// <summary>What the device's stream is doing. Set by the supervisor.</summary>
    public DeviceStreamState State
    {
        get => channel.State;
        set => channel.State = value;
    }

    /// <summary>
    /// Hands one mixed block to the device. Called by the mix thread; inside the audio path.
    /// </summary>
    /// <param name="interleaved">The bus's audio, interleaved.</param>
    /// <param name="frameCount">Frames in the block.</param>
    public void WriteBlock(ReadOnlySpan<float> interleaved, int frameCount) =>
        channel.Write(interleaved, frameCount);

    /// <summary>
    /// Fills the device's next buffer. Shaped to be handed straight to <see cref="IRenderStream.Start"/>.
    /// </summary>
    /// <remarks>
    /// Inside the audio path, on the device's own thread. A shortfall is silence and a counter,
    /// never a wait — the mix has not finished and blocking here would stop the device instead.
    /// </remarks>
    /// <param name="destination">Where to write.</param>
    /// <param name="frameCount">Frames wanted.</param>
    /// <returns>Frames written.</returns>
    public int Fill(Span<float> destination, int frameCount) => channel.Pull(destination);

    /// <summary>Advances the drift correction. Control thread, on a timer.</summary>
    /// <param name="elapsed">Time since the previous call.</param>
    /// <returns>The ratio now in force.</returns>
    public double UpdateCorrection(TimeSpan elapsed) => channel.UpdateCorrection(elapsed);

    /// <summary>This output's clock and buffer state, for the diagnostics view.</summary>
    /// <returns>The current figures.</returns>
    public DeviceTelemetry GetTelemetry() => channel.GetTelemetry();

    /// <summary>Discards everything buffered. For a device that disappeared and came back.</summary>
    public void Reset() => channel.Reset();
}
