using Vam.Engine.Diagnostics;

namespace Vam.Engine.Devices;

/// <summary>
/// How one <see cref="DeviceInputChannel"/> is built.
/// </summary>
/// <remarks>
/// A parameter object because the channel owns four collaborators that each need a figure or two,
/// and a constructor taking all of them positionally would be six arguments nobody could read at a
/// call site.
/// </remarks>
public sealed record DeviceInputChannelOptions
{
    /// <summary>The rate the device claims, and the rate the mix graph pulls at.</summary>
    public required int NominalSampleRate { get; init; }

    /// <summary>Channels interleaved in every buffer, on both sides of the ring.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Largest block the mix graph will ever ask for in one pull.</summary>
    public required int BlockFrames { get; init; }

    /// <summary>
    /// Ring capacity. Rounded up to a power of two by the ring itself, and wanted large enough that
    /// a device running fast has somewhere to put the surplus while the servo catches up.
    /// </summary>
    public required int RingCapacityFrames { get; init; }

    /// <summary>
    /// Where the servo holds the buffer. This is the latency the device contributes, so it is a
    /// deliberate figure rather than whatever the ring happens to settle at.
    /// </summary>
    public required int TargetFillFrames { get; init; }

    /// <summary>
    /// How much history the drift estimate is fitted over. A minute by default: shorter starts
    /// tracking jitter rather than drift.
    /// </summary>
    public TimeSpan EstimatorWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How often <see cref="DeviceInputChannel.UpdateCorrection"/> is expected to be called. Sizes
    /// the estimator's history, so it has to be roughly honest.
    /// </summary>
    public TimeSpan CorrectionInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Where to note a dropout, or null to only count them. I2.
    /// </summary>
    /// <remarks>
    /// A counter says a hundred and four dropouts happened. A list says whether they were one bad
    /// minute or spread across three hours, and those have completely different causes.
    /// </remarks>
    public DropoutLog? Dropouts { get; init; }

    /// <summary>Which endpoint this is, so a note in the log can be resolved back to a name.</summary>
    public int EndpointIndex { get; init; }
}
