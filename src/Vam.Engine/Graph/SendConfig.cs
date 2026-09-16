namespace Vam.Engine.Graph;

/// <summary>
/// One input-to-bus send, as the operator set it. D2 and D2a.
/// </summary>
/// <remarks>
/// Absent from the configuration means off. Only the pairs somebody has actually switched on are
/// stored, so a console of sixteen strips and six buses carries the handful of sends that exist
/// rather than ninety-six rows of nothing.
/// </remarks>
/// <param name="ChannelIndex">Which strip.</param>
/// <param name="BusIndex">Which bus.</param>
/// <param name="IsOn">The on/off the strip buttons drive. D2a.</param>
/// <param name="LevelDb">The send level. D2.</param>
public readonly record struct SendConfig(int ChannelIndex, int BusIndex, bool IsOn, double LevelDb);
