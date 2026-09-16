using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Extensions;

namespace Vam.Ui.Services;

/// <summary>
/// Gets the console talking to an engine, without asking anybody anything.
/// </summary>
/// <remarks>
/// <para>
/// Almost every console is on the machine doing the mixing, and on that machine there is exactly one
/// right answer: use the engine that is running, and if none is, start one. Putting that to somebody
/// as a question at every startup would be asking them to confirm the only option there is.
/// </para>
/// <para>
/// Connecting somewhere else is a change of address made in settings, by the few people who need it,
/// once. It is not a decision the other people should have to walk past on the way in.
/// </para>
/// <para>
/// Nothing here blocks the console from opening. It runs while the shell renders, and what it is
/// doing shows in the status bar, which already has words for connecting, reconnecting and refused.
/// </para>
/// </remarks>
public sealed class EngineConnector(
    IVamSession session,
    EngineProbe probe,
    VamSessionOptions options,
    IPlatformServices platform)
{
    /// <summary>How long a freshly started engine gets to answer.</summary>
    /// <remarks>
    /// A cold engine enumerates devices and opens them before it serves anything, so the first answer
    /// is seconds away rather than milliseconds.
    /// </remarks>
    static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often to ask, while waiting for one to come up.</summary>
    static readonly TimeSpan StartPollInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Whether the console has already done this.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>
    /// What went wrong last, as a localisation key, or empty when nothing did.
    /// </summary>
    /// <remarks>
    /// Shown in settings beside the address rather than in front of somebody's face. A console that
    /// could not start an engine is still a console, and the status bar is already saying it is not
    /// connected; this says why, where somebody who wants to know will look.
    /// </remarks>
    public string Problem { get; private set; } = string.Empty;

    /// <summary>The unshortened reason, when a host gave one. Not translated.</summary>
    public string ProblemDetail { get; private set; } = string.Empty;

    /// <summary>Raised when <see cref="Problem"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Connects to the engine on this machine, starting one if there is none.</summary>
    /// <param name="cancellationToken">Gives up waiting for an engine to come up.</param>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (HasStarted)
        {
            return;
        }

        HasStarted = true;

        string address = platform.RememberedEngine ?? options.Address;

        options.Address = address;

        if (!await probe.IsListeningAsync(address, cancellationToken).ConfigureAwait(false))
        {
            await StartHereAsync(address, cancellationToken).ConfigureAwait(false);
        }

        // Connected either way. If starting one did not work, this is the console retrying an address
        // that will answer the moment somebody puts an engine there, which is better than a console
        // that gave up and has to be restarted once they have.
        await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether this console may stop the engine it is looking at.</summary>
    /// <remarks>
    /// Only one on this machine. Stopping a remote engine is ending somebody else's meeting from
    /// across a network, and a button that can do that is a button that will eventually be pressed
    /// by somebody who thought it was theirs.
    /// </remarks>
    public bool CanStopEngine => options.Address == VamSessionOptions.LocalAddress;

    /// <summary>Whether this console may stop it and start another.</summary>
    /// <remarks>
    /// Stopping goes over the protocol and any console can do it; starting is a process, which only
    /// a host that ships the engine beside it can. Offering restart without the second half would
    /// stop the engine and leave it stopped.
    /// </remarks>
    public bool CanRestartEngine => CanStopEngine && platform.CanStartEngine;

    /// <summary>Stops the engine this console is looking at.</summary>
    /// <param name="reason">What the engine should write in its log.</param>
    /// <param name="cancellationToken">Abandons the wait, not the request.</param>
    /// <returns>Whether it was asked.</returns>
    public async ValueTask<bool> StopEngineAsync(string reason, CancellationToken cancellationToken = default)
    {
        // Asked, never killed. The engine saves the console, closes the recording files and lets go
        // of the devices on the way out, and none of that happens to a process that is simply gone.
        CommandReply reply = await session
            .ApplyAsync(new Command { Shutdown = new Shutdown { Reason = reason } }, cancellationToken)
            .ConfigureAwait(false);

        if (!reply.Accepted)
        {
            Report("settings.stopRefused", reply.Reason);
        }

        return reply.Accepted;
    }

    /// <summary>Stops the engine and starts a fresh one, then reconnects.</summary>
    /// <param name="cancellationToken">Abandons the wait.</param>
    public async ValueTask RestartEngineAsync(CancellationToken cancellationToken = default)
    {
        if (!await StopEngineAsync("Restarted from the console.", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Waited for rather than assumed. Starting a second engine while the first still holds the
        // devices gives two processes fighting over the same microphones, which sounds exactly as
        // bad as it reads.
        await WaitUntilGoneAsync(VamSessionOptions.LocalAddress, cancellationToken).ConfigureAwait(false);

        Report(string.Empty);

        await StartHereAsync(VamSessionOptions.LocalAddress, cancellationToken).ConfigureAwait(false);
        await session.ReconnectAsync(VamSessionOptions.LocalAddress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Points the console at a different engine.</summary>
    /// <param name="typed">An address or a bare host name.</param>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>True when the console was re-pointed.</returns>
    public async ValueTask<bool> SwitchToAsync(string? typed, CancellationToken cancellationToken = default)
    {
        if (typed.ToEngineAddress() is not string address)
        {
            Report("settings.notAnAddress");

            return false;
        }

        Report(string.Empty);

        platform.RememberedEngine = address;

        // Not probed first. Somebody typing an address in settings has said where the engine is, or
        // is going to be, and the status bar reports the attempt honestly either way — where the
        // startup path has to decide something, this one has been told.
        await session.ReconnectAsync(address, cancellationToken).ConfigureAwait(false);

        return true;
    }

    async ValueTask StartHereAsync(string address, CancellationToken cancellationToken)
    {
        if (!platform.CanStartEngine)
        {
            Report("settings.noEngineHere");

            return;
        }

        if (await platform.StartEngineAsync(address, cancellationToken).ConfigureAwait(false) is string problem)
        {
            Report("settings.startFailed", problem);

            return;
        }

        if (!await WaitUntilAnsweringAsync(address, cancellationToken).ConfigureAwait(false))
        {
            // It started and never answered, which is a different failure from not starting and has a
            // different answer: the engine's own log says why, and this console cannot.
            Report("settings.startedButSilent");
        }
    }

    async ValueTask WaitUntilGoneAsync(string address, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(StartPollInterval);

        DateTimeOffset giveUpAt = DateTimeOffset.UtcNow + StartTimeout;

        while (DateTimeOffset.UtcNow < giveUpAt)
        {
            if (!await probe.IsListeningAsync(address, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    async ValueTask<bool> WaitUntilAnsweringAsync(string address, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(StartPollInterval);

        DateTimeOffset giveUpAt = DateTimeOffset.UtcNow + StartTimeout;

        while (DateTimeOffset.UtcNow < giveUpAt)
        {
            if (await probe.IsListeningAsync(address, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return false;
    }

    void Report(string problem, string detail = "")
    {
        Problem = problem;
        ProblemDetail = detail;

        Changed?.Invoke();
    }
}
