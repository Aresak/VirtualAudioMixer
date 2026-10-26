using Vam.Ui.Abstractions;

namespace Vam.Ui.Extensions;

/// <summary>The handover of the one meter-frame slot between the views that draw meters.</summary>
/// <remarks>
/// A view is replaced by rendering the new one and disposing the old one afterwards, so the old
/// one's teardown runs after the new one has taken the slot and must not clear it.
/// </remarks>
public static class VamSessionExtensions
{
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
