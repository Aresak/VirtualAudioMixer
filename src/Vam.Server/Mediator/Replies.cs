using Vam.Protocol.V1;

namespace Vam.Server.Mediator;

/// <summary>
/// The two answers a handler can give.
/// </summary>
/// <remarks>
/// A refusal is a sentence rather than a code, everywhere, without exception. An operator reading
/// "that send is excluded by mix-minus" can act on it; an operator reading a status of 3 cannot, and
/// neither can whoever they telephone about it afterwards.
/// </remarks>
public static class Replies
{
    /// <summary>It was taken.</summary>
    /// <returns>The reply.</returns>
    public static CommandReply Accepted() => new() { Accepted = true, Reason = string.Empty };

    /// <summary>It was not, and here is why in words.</summary>
    /// <param name="reason">Written for a person rather than for a log.</param>
    /// <returns>The reply.</returns>
    public static CommandReply Refused(string reason) => new() { Accepted = false, Reason = reason };

    /// <summary>
    /// One of the two, as the answer a handler returns.
    /// </summary>
    /// <remarks>
    /// Every operation here is synchronous — it queues a command or rewrites a configuration on the
    /// control thread. The interface is asynchronous because a mediator handler may be, and wrapping
    /// once here is honest about which of those is true.
    /// </remarks>
    /// <param name="reply">What happened.</param>
    /// <returns>It, as a completed task.</returns>
    public static Task<CommandReply> DoneAsync(CommandReply reply) => Task.FromResult(reply);
}
