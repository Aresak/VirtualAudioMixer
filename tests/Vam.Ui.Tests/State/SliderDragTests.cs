using Vam.TestKit.Harness;
using Vam.Ui.State;
using Xunit;

namespace Vam.Ui.Tests.State;

/// <summary>
/// A dragged slider sends the latest value rather than every value, and a reply does not move a
/// slider somebody is holding.
/// </summary>
public class SliderDragTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task DraggingAcrossTheTravelSendsFarFewerCommandsThanInputEvents()
    {
        SliderDrag drag = new();
        List<double> sent = [];
        TaskCompletionSource reply = new();
        List<Task> moves = [];

        Task SendAsync(double value)
        {
            sent.Add(value);
            reply = new TaskCompletionSource();
            return reply.Task;
        }

        for (int position = 0; position <= 1000; position++)
        {
            moves.Add(drag.MoveAsync(position, SendAsync));

            // A reply every fifty input events, which is a slow engine but not an unusual one.
            if (position % 50 == 49)
            {
                reply.SetResult();
            }
        }

        while (drag.IsSending)
        {
            reply.SetResult();
        }

        await Task.WhenAll(moves);

        Assert.True(sent.Count <= 25, $"{sent.Count} commands for 1001 input events.");
        Assert.Equal(1000, sent[^1]);
        Assert.False(drag.IsSending);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task OnlyTheLatestOfTheQueuedValuesIsSent()
    {
        SliderDrag drag = new();
        List<double> sent = [];
        TaskCompletionSource reply = new();

        Task SendAsync(double value)
        {
            sent.Add(value);
            reply = new TaskCompletionSource();
            return reply.Task;
        }

        Task first = drag.MoveAsync(1, SendAsync);
        await drag.MoveAsync(2, SendAsync);
        await drag.MoveAsync(3, SendAsync);
        await drag.MoveAsync(4, SendAsync);

        reply.SetResult();
        reply.SetResult();
        await first;

        Assert.Equal<double>([1, 4], sent);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AReplyThatArrivesWhileHeldDoesNotMoveIt()
    {
        SliderDrag drag = new();
        TaskCompletionSource reply = new();

        Task SendAsync(double value)
        {
            reply = new TaskCompletionSource();
            return reply.Task;
        }

        Assert.Equal(300, drag.Draw(300));

        drag.Hold();
        Task moving = drag.MoveAsync(500, SendAsync);

        // The read-back of a command sent earlier in the drag.
        Assert.Equal(300, drag.Draw(420));

        reply.SetResult();
        await moving;

        Assert.Equal(300, drag.Draw(500));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AfterReleaseItShowsTheEngineOnceTheLastCommandIsAnswered()
    {
        SliderDrag drag = new();
        TaskCompletionSource reply = new();

        Task SendAsync(double value)
        {
            reply = new TaskCompletionSource();
            return reply.Task;
        }

        drag.Draw(0);
        drag.Hold();
        Task moving = drag.MoveAsync(800, SendAsync);
        drag.Release();

        // An older value read back while the last one is still on its way is not replayed.
        Assert.Equal(800, drag.Draw(640));

        reply.SetResult();
        await moving;

        Assert.Equal(799, drag.Draw(799));
    }
}
