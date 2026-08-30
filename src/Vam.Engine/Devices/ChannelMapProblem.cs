namespace Vam.Engine.Devices;

/// <summary>
/// One reason a channel map cannot be used, in terms a person can act on.
/// </summary>
/// <remarks>
/// Carries the source it came from rather than only a message, so a console can highlight the row
/// that is wrong instead of showing a sentence and leaving the operator to find it.
/// </remarks>
/// <param name="Kind">What is wrong.</param>
/// <param name="Source">The mapping entry at fault.</param>
/// <param name="Description">What to tell the person, naming the device and the channel.</param>
public readonly record struct ChannelMapProblem(
    ChannelMapProblemKind Kind,
    ChannelSource Source,
    string Description);
