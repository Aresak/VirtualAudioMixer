using Vam.Engine.Modifiers.BuiltIn;
using Vam.Modifiers.Abstractions;

namespace Vam.Engine.Modifiers;

/// <summary>
/// What modifiers exist, and how to make one.
/// </summary>
/// <remarks>
/// <para>
/// A factory per identifier rather than a shared instance per identifier, because two strips with
/// the same denoise are two denoises: they carry different filter histories over different audio.
/// Sharing one would make every microphone in the room process through the same envelope.
/// </para>
/// <para>
/// This is also where the third-party loader will attach when EPIC-20 arrives. Nothing loads from
/// disk yet, and the shape is deliberate: a pack registers factories here and the rest of the engine
/// never learns where a modifier came from. Retrofitting that later is how allocation discipline
/// gets lost.
/// </para>
/// </remarks>
public sealed class ModifierRegistry
{
    readonly Dictionary<string, Func<Modifier>> factories = new(StringComparer.Ordinal);

    /// <summary>Identifiers registered, in no particular order.</summary>
    public IReadOnlyCollection<string> Ids => factories.Keys;

    /// <summary>A registry holding everything built into the engine.</summary>
    /// <returns>The registry.</returns>
    public static ModifierRegistry CreateDefault()
    {
        ModifierRegistry registry = new();

        // The order they are usually chained in, which is also the order EPIC-05 argues for:
        // high-pass, gate, denoise, equaliser, adaptive gain, compressor. The VAD taps before the
        // denoise and is not a link, so it is not here.
        registry.Register("vam.highpass", static () => new HighPassModifier());
        registry.Register("vam.gate", static () => new GateModifier());
        registry.Register("vam.denoise", static () => new DenoiseModifier());
        registry.Register("vam.equaliser", static () => new EqualiserModifier());
        registry.Register("vam.adaptivegain", static () => new AdaptiveGainModifier());
        registry.Register("vam.compressor", static () => new CompressorModifier());
        registry.Register("vam.limiter", static () => new LimiterModifier());
        registry.Register("vam.gain", static () => new GainModifier());

        return registry;
    }

    /// <summary>Makes an identifier available.</summary>
    /// <param name="id">The descriptor's identifier.</param>
    /// <param name="factory">Makes a fresh instance, with its own state.</param>
    public void Register(string id, Func<Modifier> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);

        factories[id] = factory;
    }

    /// <summary>Makes one instance.</summary>
    /// <param name="id">Which modifier.</param>
    /// <returns>A fresh instance, or null when nothing is registered under that identifier.</returns>
    public Modifier? Create(string id) =>
        factories.TryGetValue(id, out Func<Modifier>? factory) ? factory() : null;
}
