namespace Vam.Engine.Graph;

/// <summary>
/// Everything the audio thread needs to know about one bus for one block.
/// </summary>
/// <param name="Gain">The bus's own level, linear.</param>
/// <param name="Role">What it is for. Changes the default tap, solo behaviour and whether a device is needed.</param>
/// <param name="ChannelCount">Channels the bus carries.</param>
/// <param name="IsMuted">Whether the bus outputs silence.</param>
public readonly record struct BusParams(
    float Gain,
    BusRole Role,
    int ChannelCount,
    bool IsMuted)
{
    /// <summary>Whether this bus takes its sources before the fader.</summary>
    /// <remarks>
    /// A monitor is pre-fader so the operator riding a fader for the stream does not change what
    /// the person wearing the headphones hears. That is the whole reason the role exists.
    /// </remarks>
    public bool IsPreFader => Role == BusRole.Monitor;

    /// <summary>Whether the solo mask applies to this bus.</summary>
    /// <remarks>
    /// Solo is an operator's monitoring tool. Letting it reach the stream bus would mean one click
    /// silences a public broadcast, which is the kind of mistake that ends up in the minutes.
    /// </remarks>
    public bool ObeysSolo => Role == BusRole.Output;
}
