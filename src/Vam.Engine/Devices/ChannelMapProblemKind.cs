namespace Vam.Engine.Devices;

/// <summary>Why a channel map was refused.</summary>
public enum ChannelMapProblemKind
{
    /// <summary>The device the source names is not among the devices present.</summary>
    DeviceAbsent,

    /// <summary>The source reads a channel the device does not have.</summary>
    ChannelOutOfRange,

    /// <summary>Two sources feed the same strip, so which one wins would be arbitrary.</summary>
    StripClaimedTwice,

    /// <summary>The source's own numbers do not describe a run of channels.</summary>
    MalformedSource
}
