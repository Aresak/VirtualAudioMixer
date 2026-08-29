namespace Vam.Core;

/// <summary>
/// Base type for every exception VAM raises itself.
/// </summary>
/// <remarks>
/// <para>
/// One base means a caller can tell "VAM decided this is wrong" from "something underneath
/// us failed" without listing types. A handler that catches this catches the engine's own
/// refusals and nothing else; an <see cref="IOException"/> from a failing disk still reaches
/// the code that knows what to do about a disk.
/// </para>
/// <para>
/// Abstract on purpose. Every throw site names what actually went wrong, so there is never
/// a bare <c>VamException</c> to catch and puzzle over.
/// </para>
/// <para>
/// Nothing in the audio path throws. The audio thread cannot handle an exception, so a fault
/// there sets a flag that the control thread reads and acts on. See <c>docs/audio-path.md</c>.
/// </para>
/// </remarks>
public abstract class VamException : Exception
{
    /// <summary>Creates the exception with a message describing what VAM refused and why.</summary>
    /// <param name="message">What went wrong, in terms an operator could act on.</param>
    protected VamException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception, wrapping the failure underneath it.</summary>
    /// <param name="message">What went wrong, in terms an operator could act on.</param>
    /// <param name="innerException">The failure this one is explaining.</param>
    protected VamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
