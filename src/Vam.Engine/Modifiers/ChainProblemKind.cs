namespace Vam.Engine.Modifiers;

/// <summary>Why a chain was refused when it was built.</summary>
public enum ChainProblemKind
{
    /// <summary>A link will not accept the channel count the link before it produces.</summary>
    ChannelCountMismatch,

    /// <summary>More links than the bypass mask can address.</summary>
    TooManyLinks,

    /// <summary>A link that is not allowed to move was asked to.</summary>
    AnchorMoved
}
