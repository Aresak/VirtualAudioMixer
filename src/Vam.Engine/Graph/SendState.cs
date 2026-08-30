namespace Vam.Engine.Graph;

/// <summary>Why one input-to-bus send is at the level it is.</summary>
/// <remarks>
/// The audio thread never reads this — off, excluded and muted all collapse to a gain of zero
/// before the snapshot is published, so the mix has no special case for any of them. This exists so
/// the console can tell an operator <i>why</i> a send is silent, which is a different question and
/// belongs on a different thread.
/// </remarks>
public enum SendState
{
    /// <summary>The operator switched it off. D2a.</summary>
    Off,

    /// <summary>Carrying audio at its send level. D2.</summary>
    On,

    /// <summary>
    /// Excluded by mix-minus, and not switchable. D4. The bus feeds a device whose own microphone
    /// this is, so sending it there would play somebody their own voice, late.
    /// </summary>
    ExcludedMixMinus
}
