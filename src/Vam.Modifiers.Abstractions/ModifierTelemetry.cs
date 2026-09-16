namespace Vam.Modifiers.Abstractions;

/// <summary>
/// What a modifier is doing right now, for the meters.
/// </summary>
/// <remarks>
/// <para>
/// Written by the modifier on the audio thread, straight into a field the host owns, and read off
/// thread whenever a meter frame is built. That is why it is a struct passed by <c>ref</c> rather
/// than an event or a callback: a compressor's gain reduction reaches the strip's meter with no
/// allocation, no delegate and no queue.
/// </para>
/// <para>
/// Deliberately tiny and deliberately not extensible. It is part of the ABI a third-party modifier
/// compiles against, so a field added here is a field every existing modifier has to be rebuilt for.
/// </para>
/// </remarks>
public struct ModifierTelemetry
{
    /// <summary>How much gain the modifier is taking away, in decibels. Zero or negative.</summary>
    public float GainReductionDb;

    /// <summary>The level the modifier is seeing, in decibels relative to full scale.</summary>
    public float LevelDb;

    /// <summary>Whether the modifier considers itself to be doing something this block.</summary>
    /// <remarks>
    /// A gate that is closed and a compressor that is not compressing both say false, which is what
    /// lets a console grey out a control rather than showing a meter that never moves.
    /// </remarks>
    public bool IsActive;
}
