namespace Vam.Engine.Diagnostics;

/// <summary>
/// One thing that went wrong, in a form the audio thread is allowed to write. I2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fixed size and entirely free of strings.</b> A logging call from the audio thread allocates,
/// may format a message and may take a lock, and doing it at the moment something is already going
/// wrong is how one dropout becomes several. So the audio thread writes numbers and an index, and
/// the pump on the control thread turns them into words.
/// </para>
/// <para>
/// <b>A list you can read afterwards, not a counter.</b> A number that says a hundred and four
/// dropouts happened tells an operator nothing about whether they were one bad minute or spread
/// across three hours, and those have completely different causes.
/// </para>
/// </remarks>
/// <param name="TimestampTicks">When, from the system clock.</param>
/// <param name="EndpointIndex">Which device or bus, by its place in the registry.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Frames">How many frames it cost.</param>
/// <param name="Detail">One number whose meaning depends on the kind - a fill level, a ratio.</param>
public readonly record struct DropoutRecord(
    long TimestampTicks,
    int EndpointIndex,
    DropoutKind Kind,
    int Frames,
    float Detail);
