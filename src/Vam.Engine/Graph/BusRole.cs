namespace Vam.Engine.Graph;

/// <summary>What a bus is for.</summary>
/// <remarks>
/// <b>Monitors are buses.</b> The role changes exactly three behaviours — which tap a send takes by
/// default, whether the bus obeys the solo mask, and whether it needs an output device — and nothing
/// else. That is what makes "add a bus" and "add a monitor" one code path instead of two.
/// </remarks>
public enum BusRole
{
    /// <summary>An ordinary output bus. Post-fader, obeys solo, wants a device.</summary>
    Output,

    /// <summary>
    /// A headphone feed for somebody in the room. Pre-fader by default, so the operator moving a
    /// fader does not change what the person in the chair hears. D5.
    /// </summary>
    Monitor,

    /// <summary>The feed going out to the world. D3. Post-fader, and the one that must never break.</summary>
    Stream
}
