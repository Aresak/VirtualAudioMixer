using Vam.Engine.Graph;
using Vam.Engine.Modifiers;
using Vam.Protocol.V1;

namespace Vam.Server.Services;

/// <summary>
/// Saving, applying and deleting chain presets. B0d and B12.
/// </summary>
/// <remarks>
/// A preset is a whole chain rather than a set of numbers, so applying one replaces the chain and
/// recompiles. That is the difference B0d is about: "Jabra shared" and "Studio 180 degrees" are
/// different objects, one with a denoise in it and one without, and no amount of parameter copying
/// gets from one to the other.
/// </remarks>
public static class PresetCommands
{
    /// <summary>Saves a chain under a name.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="store">Where the presets live.</param>
    /// <param name="request">Whose chain, and what to call it.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply Save(GraphController graph, ChainPresetStore store, SaveChainPreset request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Refuse("A preset needs a name.");
        }

        if (Chain(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        store.Save(request.Name, chain);

        // The strip now belongs to the preset it was just saved as, so the console stops showing it
        // as modified the moment it is saved.
        Remember(graph, request.Target, request.Name);

        return Accept();
    }

    /// <summary>Replaces a chain with a preset.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="store">Where the presets live.</param>
    /// <param name="request">Whose chain, and which preset.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply Apply(GraphController graph, ChainPresetStore store, ApplyChainPreset request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);

        if (store.Find(request.Name) is not { } preset)
        {
            return Refuse($"There is no preset called '{request.Name}'.");
        }

        if (Chain(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        // ToChain mints new link identities, so every modifier in the chain is built fresh. That is
        // correct here and only here: applying a preset is meant to be a clean start, unlike a
        // reorder, which must keep its instances so a denoise does not restart mid-sentence.
        chain.Clear();
        chain.AddRange(preset.ToChain());

        Remember(graph, request.Target, request.Name);
        graph.Recompile();

        return Accept();
    }

    /// <summary>Removes a preset from the library.</summary>
    /// <remarks>The chains that came from it are untouched, and keep working.</remarks>
    /// <param name="store">Where the presets live.</param>
    /// <param name="request">Which preset.</param>
    /// <returns>Whether it was there.</returns>
    public static CommandReply Delete(ChainPresetStore store, DeleteChainPreset request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);

        return store.Delete(request.Name)
            ? Accept()
            : Refuse($"There is no preset called '{request.Name}'.");
    }

    /// <summary>Lists what the engine has.</summary>
    /// <param name="store">Where the presets live.</param>
    /// <returns>The presets, with enough of each to show in a list.</returns>
    public static ChainPresetList List(ChainPresetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        ChainPresetList list = new();

        foreach (ChainPreset preset in store.All())
        {
            ChainPresetSummary summary = new()
            {
                Name = preset.Name,
                LinkCount = preset.Links.Count
            };

            foreach (ModifierSetting link in preset.Links)
            {
                summary.ModifierIds.Add(link.ModifierId);
            }

            list.Presets.Add(summary);
        }

        return list;
    }

    static List<ModifierSetting>? Chain(GraphController graph, ChainTarget? target)
    {
        if (target is null)
        {
            return null;
        }

        if (target.IsBus)
        {
            return target.Index >= 0 && target.Index < graph.Config.Buses.Count
                ? graph.Config.Buses[target.Index].Chain
                : null;
        }

        return target.Index >= 0 && target.Index < graph.Config.Channels.Count
            ? graph.Config.Channels[target.Index].Chain
            : null;
    }

    static void Remember(GraphController graph, ChainTarget target, string name)
    {
        if (target.IsBus)
        {
            graph.Config.Buses[target.Index] = graph.Config.Buses[target.Index] with { PresetName = name };
            return;
        }

        graph.Config.Channels[target.Index] = graph.Config.Channels[target.Index] with { PresetName = name };
    }

    static CommandReply Missing(ChainTarget? target) =>
        Refuse(target is null
            ? "The command did not say whose chain to change."
            : $"There is no {(target.IsBus ? "bus" : "strip")} {target.Index}.");

    static CommandReply Refuse(string reason) => new() { Accepted = false, Reason = reason };

    static CommandReply Accept() => new() { Accepted = true, Reason = string.Empty };
}
