namespace Vam.Engine.Graph;

/// <summary>What a control-thread command changes.</summary>
public enum GraphCommandKind
{
    /// <summary>Input trim. A8.</summary>
    ChannelTrim,

    /// <summary>Fader position. B8.</summary>
    ChannelFader,

    /// <summary>Mute, solo, polarity or mono fold, set or cleared.</summary>
    ChannelFlag,

    /// <summary>A bus's own level.</summary>
    BusGain,

    /// <summary>Whether a bus outputs silence.</summary>
    BusMuted,

    /// <summary>One input-to-bus send. D2 and D2a.</summary>
    Send
}
