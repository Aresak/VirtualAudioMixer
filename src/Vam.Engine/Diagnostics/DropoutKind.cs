namespace Vam.Engine.Diagnostics;

/// <summary>What went wrong for one block. I2.</summary>
/// <remarks>
/// An enum rather than a message, because the audio thread records these and the audio thread may
/// not build a string. The words are added by the pump on the other side.
/// </remarks>
public enum DropoutKind
{
    /// <summary>A device produced audio faster than it could be taken away.</summary>
    CaptureOverrun,

    /// <summary>The mix asked a device for audio that had not arrived.</summary>
    CaptureUnderrun,

    /// <summary>An output asked for audio the mix had not finished.</summary>
    RenderUnderrun,

    /// <summary>The recorder could not hand a block over because the disk was behind.</summary>
    RecordingDropped,

    /// <summary>The drift correction hit its limit, so something other than drift is wrong.</summary>
    CorrectionClamped
}
