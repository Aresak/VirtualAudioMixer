using Vam.Engine.Devices.Abstractions;

namespace Vam.Engine.Graph;

/// <summary>
/// The whole console, as configuration. What the compiler turns into a plan and a snapshot.
/// </summary>
/// <remarks>
/// Control thread only, and freely mutable. Nothing here is ever read by the audio thread — that is
/// what compilation is for.
/// </remarks>
public sealed class GraphConfig
{
    /// <summary>Input strips, in console order.</summary>
    public List<ChannelConfig> Channels { get; } = [];

    /// <summary>Buses, in console order.</summary>
    public List<BusConfig> Buses { get; } = [];

    /// <summary>Sends the operator has switched on. Anything absent is off.</summary>
    public List<SendConfig> Sends { get; } = [];

    /// <summary>How far a strip nobody is speaking into is turned down. C3.</summary>
    public double AutomixDepthDb { get; set; } = -15.0;

    /// <summary>How quickly gain sharing follows the conversation.</summary>
    public double AutomixResponseMilliseconds { get; set; } = 120.0;

    /// <summary>Whether the automixer is switched out entirely. C10.</summary>
    public bool IsAutomixBypassed { get; set; } = true;

    /// <summary>
    /// Devices whose microphone and speaker belong to the same person, for mix-minus.
    /// </summary>
    public List<EndpointPair> EndpointPairs { get; } = [];

    /// <summary>
    /// Which bus feeds the device keeping time, and therefore reaches the render callback directly.
    /// </summary>
    public int PrimaryBusIndex { get; set; }

    /// <summary>Channels the primary output device presents.</summary>
    public int PrimaryOutputChannelCount { get; set; } = 2;

    /// <summary>
    /// Which device each strip's audio arrives on, in the master clock's pull order.
    /// </summary>
    /// <remarks>
    /// The graph addresses devices by their position in that order rather than by identity, because
    /// by the time the audio thread is running, identity has already been resolved and looking it up
    /// again per block would be a dictionary in the render path.
    /// </remarks>
    public List<AudioDeviceId> InputDeviceOrder { get; } = [];
}
