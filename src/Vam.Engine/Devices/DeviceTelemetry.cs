using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Devices;

/// <summary>
/// One device's clock and buffer state, as the strip header and the diagnostics view read it.
/// </summary>
/// <remarks>
/// <para>
/// A struct so a caller can poll every device into a span it already owns and allocate nothing -
/// this is read at meter rate, and a class here would be one collection per poll for the garbage
/// collector to deal with.
/// </para>
/// <para>
/// <b>Values may be up to one correction period stale.</b> They are assembled from volatile reads
/// of counters several threads write, without taking a lock anywhere, because a consistent snapshot
/// is not worth making the audio thread wait for. A rate that is a quarter of a second out of date
/// is fine for a number a person reads.
/// </para>
/// </remarks>
/// <param name="NominalSampleRate">The rate the device claims, and the rate it is configured at.</param>
/// <param name="MeasuredSampleRate">The rate it is really running at, as the drift estimator sees it.</param>
/// <param name="DriftPpm">How far the two sit apart, in parts per million. Positive runs fast.</param>
/// <param name="Ratio">Input frames consumed per output frame. The correction actually being applied.</param>
/// <param name="FillPercentage">How full the ring is, from 0 to 100.</param>
/// <param name="OverrunCount">Buffers the device produced that would not fit. Monotonic.</param>
/// <param name="UnderrunCount">Frames the mix graph asked for and got silence instead. Monotonic.</param>
/// <param name="State">What the stream is currently doing.</param>
public readonly record struct DeviceTelemetry(
    int NominalSampleRate,
    double MeasuredSampleRate,
    double DriftPpm,
    double Ratio,
    double FillPercentage,
    long OverrunCount,
    long UnderrunCount,
    DeviceStreamState State);
