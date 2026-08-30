namespace Vam.Engine.Graph;

/// <summary>
/// One step of the mix, run once per block in a fixed order.
/// </summary>
/// <remarks>
/// <para>
/// <b>An abstract class rather than an interface, and every node <c>sealed</c>.</b> An interface
/// call on a value type boxes, and dispatch happens once per node per block; the base class is the
/// allocation-free choice and the audio-path rule outranks the usual preference for interfaces.
/// Sealing lets the JIT devirtualise the leaf calls inside the walk.
/// </para>
/// <para>
/// <b>A node never throws.</b> The audio thread has nowhere to put an exception, and a node with
/// nothing to do writes silence instead. A strip whose device failed is handled by its parameters
/// carrying <see cref="ChannelFlags.Faulted"/> and mixing to zero, not by anything here failing.
/// </para>
/// <para>
/// Nodes hold their own state — filter histories, envelopes, cursors — and that state is allocated
/// when the plan is compiled. It deliberately does not live in the snapshot: putting it there would
/// mean copying every filter history on every fader move.
/// </para>
/// </remarks>
public abstract class AudioNode
{
    /// <summary>Runs this node for one block. Inside the audio path.</summary>
    /// <param name="context">The block's buffers and parameters.</param>
    public abstract void Process(ref RenderContext context);

    /// <summary>
    /// Clears whatever this node remembers between blocks.
    /// </summary>
    /// <remarks>
    /// Control thread. For a device that disappeared and came back, or a plan being installed: the
    /// old history describes audio that is no longer arriving, and carrying it across would splice
    /// two unrelated moments together.
    /// </remarks>
    public virtual void Reset()
    {
    }
}
