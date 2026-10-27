namespace Vam.Ui.State;

/// <summary>
/// What one slider sends while it is moved, and what it draws while it is held.
/// </summary>
/// <remarks>
/// <para>
/// An input event arrives for every pixel the pointer travels, and each command is answered with a
/// read-back of the whole console. Sending one per event queues dozens behind the pointer, and the
/// cap then replays them after it is let go. So at most one command is in flight, and a newer value
/// replaces the one waiting behind it.
/// </para>
/// <para>
/// While held, the slider draws what it drew when it was taken hold of, which leaves the browser's
/// own position alone. After release it draws the last value moved to until the last command has
/// been answered, and then the engine's.
/// </para>
/// <para>
/// Not thread-safe, and does not need to be: a component's events arrive on its renderer's
/// dispatcher one at a time.
/// </para>
/// </remarks>
public sealed class SliderDrag
{
    double? queued;
    double latest;
    double drawn;
    bool isSettling;

    /// <summary>Whether the pointer is holding the slider.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Whether a command is in flight.</summary>
    public bool IsSending { get; private set; }

    /// <summary>The value to draw, given what the engine last reported.</summary>
    /// <param name="engineValue">The engine's value.</param>
    /// <returns>The value to put on the slider.</returns>
    public double Draw(double engineValue)
    {
        if (!IsHeld)
        {
            drawn = IsSending || isSettling ? latest : engineValue;
            isSettling = false;
        }

        return drawn;
    }

    /// <summary>The pointer took hold of the slider.</summary>
    public void Hold()
    {
        IsHeld = true;
        latest = drawn;
    }

    /// <summary>The pointer let go of the slider.</summary>
    /// <remarks>
    /// The next draw is the value let go at and the one after it the engine's. Going straight to the
    /// engine's would draw nothing when it still equals the value from before the hold, and the
    /// browser would keep showing a position the engine refused.
    /// </remarks>
    public void Release()
    {
        if (IsHeld)
        {
            IsHeld = false;
            isSettling = true;
        }
    }

    /// <summary>
    /// Sends <paramref name="value"/>, or queues it behind the command already in flight.
    /// </summary>
    /// <param name="value">Where the slider was moved to.</param>
    /// <param name="send">Sends one value to the engine.</param>
    /// <returns>
    /// A task that completes at once when the value was queued, and otherwise when every value queued
    /// behind it has been sent.
    /// </returns>
    public async Task MoveAsync(double value, Func<double, Task> send)
    {
        latest = value;

        if (IsSending)
        {
            queued = value;
            return;
        }

        IsSending = true;

        try
        {
            double? next = value;

            while (next is { } current)
            {
                queued = null;
                await send(current);
                next = queued;
            }
        }
        finally
        {
            queued = null;
            IsSending = false;
        }
    }
}
