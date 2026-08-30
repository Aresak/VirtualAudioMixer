using Vam.Engine.Graph;
using Vam.Engine.Modifiers;
using Vam.Protocol.V1;

namespace Vam.Server.Services;

/// <summary>
/// The five commands that edit a modifier chain, for a strip or for a bus. B0 and D6.
/// </summary>
/// <remarks>
/// <para>
/// One implementation for both, because a bus chain is a strip chain on a summed signal and nothing
/// about editing it differs. The target says which; the code below is the only place that has to
/// know the difference exists.
/// </para>
/// <para>
/// Every one of these recompiles. Adding a link changes the shape of the plan, and so does removing
/// or moving one — a reorder that reused the old plan would run the links in the old order.
/// </para>
/// </remarks>
public static class ChainCommands
{
    /// <summary>Switches a link out, or back in.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">What to change.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply SetBypass(GraphController graph, SetModifierBypass request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        if (Resolve(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"There is no link {request.LinkIndex} there.");
        }

        chain[request.LinkIndex] = chain[request.LinkIndex] with { IsBypassed = request.Bypassed };
        graph.Recompile();

        return Accept();
    }

    /// <summary>Sets one parameter of one link.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">What to change.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply SetParameter(GraphController graph, SetModifierParameter request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        if (Resolve(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"There is no link {request.LinkIndex} there.");
        }

        chain[request.LinkIndex].Values[request.ParameterId] = (float)request.Value;
        graph.Recompile();

        return Accept();
    }

    /// <summary>Adds a link.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="registry">What this engine has to offer.</param>
    /// <param name="request">What to add and where.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply Add(GraphController graph, ModifierRegistry registry, AddModifier request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        if (Resolve(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        if (registry.Create(request.ModifierId) is null)
        {
            // Named rather than ignored. A console offering a modifier this engine does not have is
            // a console talking to a different build, and saying so is more useful than a gap.
            return Refuse($"This engine has no modifier called '{request.ModifierId}'.");
        }

        chain.Insert(
            Math.Clamp(request.AtIndex, 0, chain.Count),
            new ModifierSetting { LinkId = Guid.NewGuid().ToString("n"), ModifierId = request.ModifierId });

        graph.Recompile();

        return Accept();
    }

    /// <summary>Removes a link.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">Which link.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply Remove(GraphController graph, RemoveModifier request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        if (Resolve(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        if (request.LinkIndex < 0 || request.LinkIndex >= chain.Count)
        {
            return Refuse($"There is no link {request.LinkIndex} there.");
        }

        chain.RemoveAt(request.LinkIndex);
        graph.Recompile();

        return Accept();
    }

    /// <summary>Moves a link.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">From where to where.</param>
    /// <returns>Whether it was taken, and why not.</returns>
    public static CommandReply Move(GraphController graph, MoveModifier request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);

        if (Resolve(graph, request.Target) is not { } chain)
        {
            return Missing(request.Target);
        }

        if (request.FromIndex < 0 || request.FromIndex >= chain.Count
            || request.ToIndex < 0 || request.ToIndex >= chain.Count)
        {
            return Refuse("A link cannot be moved to a place that is not there.");
        }

        // Order is the configuration, not an incidental list order: a gate before a denoise and a
        // gate after one are different microphones. B0.
        ModifierSetting moving = chain[request.FromIndex];

        chain.RemoveAt(request.FromIndex);
        chain.Insert(request.ToIndex, moving);

        graph.Recompile();

        return Accept();
    }

    static List<ModifierSetting>? Resolve(GraphController graph, ChainTarget? target)
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

    static CommandReply Missing(ChainTarget? target) =>
        Refuse(target is null
            ? "The command did not say whose chain to change."
            : $"There is no {(target.IsBus ? "bus" : "strip")} {target.Index}.");

    static CommandReply Refuse(string reason) => new() { Accepted = false, Reason = reason };

    static CommandReply Accept() => new() { Accepted = true, Reason = string.Empty };
}
