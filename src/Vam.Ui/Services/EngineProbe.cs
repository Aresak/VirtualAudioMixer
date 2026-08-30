using Grpc.Core;
using Grpc.Net.Client;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;

namespace Vam.Ui.Services;

/// <summary>
/// Asks one question: is there an engine at this address, right now?
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="VamSessionClient"/> because the two want opposite things.
/// A session that has been told where the engine is should retry forever, through a network drop
/// and a restart, and never give up on an address an operator chose. A console deciding whether it
/// has an engine at all needs an answer in under a second so it can ask a question instead.
/// </para>
/// <para>
/// It calls <c>Hello</c> rather than opening a socket, so a yes means "a VAM engine that accepts
/// this protocol version", not "something is listening on that port". Those differ on a machine
/// where the port is taken by something else, which is exactly when a wrong answer costs the most.
/// </para>
/// </remarks>
public sealed class EngineProbe(VamSessionOptions options, IPlatformServices platform)
{
    /// <summary>How long to wait before deciding nothing is there.</summary>
    /// <remarks>
    /// A local engine answers in milliseconds. This is long enough for one across a slow network and
    /// short enough that a person watching a window open does not read it as the application hanging.
    /// </remarks>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Whether an engine is answering.</summary>
    /// <param name="address">Where to look.</param>
    /// <param name="cancellationToken">Gives up early.</param>
    /// <returns>True when an engine answered and accepted this console.</returns>
    public async ValueTask<bool> IsListeningAsync(string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        try
        {
            using GrpcChannel channel = GrpcChannel.ForAddress(address);

            Mixer.MixerClient client = new(channel);

            await client.HelloAsync(
                    new HelloRequest
                    {
                        ProtocolVersion = options.ProtocolVersion,
                        ClientName = platform.ClientName
                    },
                    deadline: DateTime.UtcNow.Add(Timeout),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (RpcException)
        {
            // Every no arrives this way: nothing listening, something listening that is not an
            // engine, an engine that refused this protocol version, or the deadline. They are all
            // the same answer to the only question being asked.
            return false;
        }
        catch (Exception failure) when (failure is InvalidOperationException or UriFormatException or HttpRequestException)
        {
            // A malformed address reaches this far, because the channel is built lazily.
            return false;
        }
    }
}
