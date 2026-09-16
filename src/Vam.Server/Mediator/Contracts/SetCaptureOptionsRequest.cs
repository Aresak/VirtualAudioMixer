using Shiny.Mediator;
using Vam.Protocol.V1;

namespace Vam.Server.Mediator.Contracts;

/// <summary>Changes what a recording session writes. E3.</summary>
/// <remarks>
/// It takes effect at the next recording. Tracks cannot be added to files that are already open, so
/// a session already running keeps what it started with and the console says so rather than
/// accepting a change it cannot make.
/// </remarks>
/// <param name="Inputs">Whether every input is written, before any processing.</param>
/// <param name="StreamBus">Whether the primary bus is written beside them.</param>
/// <param name="AllBuses">Whether every other bus is written as well.</param>
/// <param name="Format">Which of the engine's formats to write.</param>
public sealed record SetCaptureOptionsRequest(bool Inputs, bool StreamBus, bool AllBuses, string Format)
    : IRequest<CommandReply>;
