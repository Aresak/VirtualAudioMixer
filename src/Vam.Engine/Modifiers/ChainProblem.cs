namespace Vam.Engine.Modifiers;

/// <summary>
/// One reason a chain cannot be built, naming the link at fault.
/// </summary>
/// <remarks>
/// Naming the link is the point. "Channel count mismatch" tells an operator nothing they can act on;
/// "Denoise takes one channel and the link before it produces two" tells them which one to remove.
/// </remarks>
/// <param name="Kind">What is wrong.</param>
/// <param name="LinkIndex">Which link, counting from the head.</param>
/// <param name="Description">What to tell the person, naming the modifier.</param>
public readonly record struct ChainProblem(ChainProblemKind Kind, int LinkIndex, string Description);
