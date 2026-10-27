using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;

namespace Vam.Ui.Extensions;

/// <summary>What a view asks of a session that is not part of the connection itself.</summary>
/// <remarks>
/// The meter-frame slot is handed over between views: a view is replaced by rendering the new one
/// and disposing the old one afterwards, so the old one's teardown runs after the new one has taken
/// the slot and must not clear it.
/// </remarks>
public static class VamSessionExtensions
{
    /// <summary>Whether the engine has a feature this console would otherwise assume.</summary>
    /// <param name="session">The session.</param>
    /// <param name="feature">One of <see cref="Vam.Protocol.EngineFeatures"/>.</param>
    public static bool Supports(this IVamSession session, string feature)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Features.Contains(feature);
    }

    /// <summary>What the next recording captures, or null when the engine is too old to say.</summary>
    /// <param name="session">The session.</param>
    public static RecordingCapture? RecordingCaptures(this IVamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Supports(EngineFeatures.RecordingCaptures) ? session.Console?.Recording?.Captures : null;
    }

    /// <summary>The engine's paths, or null when it is not connected or too old to send them.</summary>
    /// <param name="session">The session.</param>
    public static EnginePaths? EnginePaths(this IVamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Supports(EngineFeatures.EnginePaths) ? session.Console?.Paths : null;
    }

    /// <summary>
    /// Whether past sessions can be asked for. True before connecting, when there is no engine yet
    /// to be older than this console.
    /// </summary>
    /// <param name="session">The session.</param>
    public static bool CanListPastSessions(this IVamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Connection != ConnectionState.Connected || session.Supports(EngineFeatures.PastSessions);
    }

    /// <summary>Whether past sessions can be deleted, renamed and exported. #99.</summary>
    /// <param name="session">The session.</param>
    public static bool CanManagePastSessions(this IVamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Supports(EngineFeatures.ManagePastSessions);
    }

    /// <summary>Whether the connected engine lacks something this console knows, so is older than it.</summary>
    /// <param name="session">The session.</param>
    public static bool IsOlderEngine(this IVamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Connection == ConnectionState.Connected
            && !EngineFeatures.All.All(session.Features.Contains);
    }

    /// <summary>Points the meter stream at this handler, displacing whoever had it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="handler">The view's handler, held in a field so it can be released by identity.</param>
    public static void TakeMeterFrames(this IVamSession session, MeterFrameHandler handler)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.MeterFrame = handler;
    }

    /// <summary>Lets go of the slot, but only if this handler still holds it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="handler">The handler given to <see cref="TakeMeterFrames"/>.</param>
    public static void ReleaseMeterFrames(this IVamSession session, MeterFrameHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (handler is not null && ReferenceEquals(session.MeterFrame, handler))
        {
            session.MeterFrame = null;
        }
    }
}
