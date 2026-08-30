namespace Vam.Engine.Dsp;

/// <summary>
/// Something that removes steady noise from speech.
/// </summary>
/// <remarks>
/// <para>
/// The seam RNNoise will arrive through. EPIC-05 specifies RNNoise via P/Invoke; what ships first is
/// a managed spectral subtraction behind this interface, labelled honestly, so the native library
/// can be dropped in later without anything above it changing.
/// </para>
/// <para>
/// The seam exists now rather than later on purpose. Retrofitting an extension point into a realtime
/// path is how allocation discipline gets lost, and a suppressor that has to be told its frame size
/// at construction is a very different shape from one that does not.
/// </para>
/// </remarks>
public interface INoiseSuppressor
{
    /// <summary>What to call it in the console, so an operator knows which one is running.</summary>
    string Name { get; }

    /// <summary>Delay it introduces, in samples. Declared so the automixer can align around it.</summary>
    int LatencySamples { get; }

    /// <summary>
    /// Suppresses noise in one block, in place. Audio thread.
    /// </summary>
    /// <param name="samples">The block.</param>
    /// <param name="strength">How much to remove, from zero to one.</param>
    void Process(Span<float> samples, float strength);

    /// <summary>Forgets everything learned about the noise so far.</summary>
    void Reset();
}
