namespace Vam.Modifiers.Abstractions;

/// <summary>
/// One processing unit in a channel's or a bus's chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole contract in three methods, and the split between them is the important part.</b>
/// <see cref="Prepare"/> runs on the control thread when the chain is built and is where a modifier
/// allocates everything it will ever need. <see cref="Process"/> runs on the audio thread and may
/// allocate nothing, lock nothing and wait for nothing. <see cref="Reset"/> runs on the control
/// thread and throws away whatever the modifier remembers.
/// </para>
/// <para>
/// <b>A modifier never throws from <see cref="Process"/>.</b> The audio thread has nowhere to put an
/// exception. A modifier that cannot do its job leaves the audio alone.
/// </para>
/// <para>
/// An abstract class rather than an interface, because an interface call on a value type boxes and
/// this is dispatched once per link per block. Sealing the implementations lets the JIT devirtualise
/// the leaf calls.
/// </para>
/// <para>
/// This type is a permanent licence-bearing ABI. A third-party modifier compiles against it and
/// against nothing else, so every addition here is a change every existing modifier has to be
/// rebuilt for. Be conservative.
/// </para>
/// </remarks>
public abstract class Modifier
{
    /// <summary>What this modifier is and what it needs.</summary>
    public abstract ModifierDescriptor Descriptor { get; }

    /// <summary>The knobs it exposes, in ordinal order. The audio thread reads parameters by this order.</summary>
    public abstract ReadOnlySpan<ParameterDescriptor> Parameters { get; }

    /// <summary>
    /// Allocates everything the modifier will need. Control thread, at chain build time.
    /// </summary>
    /// <remarks>
    /// Called again whenever the chain is recompiled, so it must be safe to call more than once and
    /// must not assume it is starting from nothing.
    /// </remarks>
    /// <param name="sampleRate">The rate audio will arrive at.</param>
    /// <param name="maxFrames">Largest block it will ever be handed.</param>
    /// <param name="channelCount">Channels it will be handed.</param>
    public abstract void Prepare(int sampleRate, int maxFrames, int channelCount);

    /// <summary>
    /// Processes one block, in place. Audio thread.
    /// </summary>
    /// <remarks>
    /// No allocation, no lock, no wait, no string, no exception. See the engine's
    /// <c>docs/audio-path.md</c>, which applies to everything reachable from here.
    /// </remarks>
    /// <param name="context">The block, its parameters and where to report what happened.</param>
    public abstract void Process(ref ModifierContext context);

    /// <summary>
    /// Discards filter histories, envelopes and anything else carried between blocks.
    /// </summary>
    /// <remarks>
    /// Control thread. For a device that came back, or a chain being rebuilt: the old history
    /// describes audio that is no longer arriving.
    /// </remarks>
    public virtual void Reset()
    {
    }
}
