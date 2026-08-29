# What counts as the audio path

The first rule is that nothing in the audio path allocates, locks or waits. The rule is
meaningless without a boundary, so here is the boundary. It is a judgement made once,
deliberately, and written down — not re-argued in every review.

## The rule

Inside the audio path there is no `new`, no LINQ, no string of any kind, no boxing, no closure,
no `async`, no lock, and no call that can block. Buffers are pooled and pre-allocated when the
graph is built. Parameter changes arrive as immutable snapshots swapped in with one interlocked
write; the audio thread only ever reads.

Not because allocation is slow — it usually is not — but because allocation eventually causes a
collection, and a collection pause during a three-hour meeting is a click on the recording of a
public session.

## What is inside

Two regions, and everything reachable from either of them:

**The render path.** The mix thread's per-block work, from the moment it takes the current
snapshot to the moment it returns. That is the whole mix graph: the head stage, every modifier's
`Process`, the automixer, the send matrix, bus chains, the limiter, and the meter and cost
counters those write into.

**Each device thread's copy loop.** From the instant a device's wait returns with a buffer, to
the instant that thread goes back to waiting. That is format conversion and the ring buffer
write.

Both sides of every ring buffer are inside — the device thread writing and the mix thread
reading.

## What is outside

The control loop that turns commands into a new snapshot. Configuration and its file. Every
logger call. Anything touching a socket. The telemetry pump that turns counters into meter
frames. The recording writer thread. The drift estimator and its servo. Sentry, and every span
it opens.

All of these are allowed to allocate freely, and most of them do. That is the point of the seam:
the expensive, convenient code lives on one side of it and the audio lives on the other.

## Deciding a case

| Method | Inside? | Why |
|---|---|---|
| `MixGraph.Process` | **Yes** | It *is* the render path. |
| `CompressorModifier.Process` | **Yes** | Reachable from the render path. Its `Prepare` is not — that runs on the control thread and is where a modifier does its allocating. |
| `AudioRingBuffer.Read` and `.TryWrite` | **Yes** | Both sides. A lock here would make the device thread wait on the mix thread, or the reverse. |
| `MeterPublisher.PublishFrame` | **No** | Runs on a timer, reads counters the audio thread wrote, formats a frame. It allocates and that is fine. |
| `DeviceSupervisor.HandleDeviceRemoved` | **No** | Runs on the control thread. It publishes a snapshot with that strip muted; the audio thread only ever sees the new snapshot. |
| `DriftEstimator.Observe` | **No** | It reads a ring's fill level on a timer. Touching a structure the audio thread also uses does not put it inside — *who runs it* decides, not *what it touches*. |

That last row is the one people get wrong. The question is never "does this data belong to the
audio thread". It is "can this run while the callback is waiting for it".

## The ambiguous one, and how it is resolved

**NAudio's own capture callback allocates.** We call into a third-party device layer that
creates objects on the very thread whose timing we depend on, and we cannot change it.

Taken literally, that would mean the rule is already broken and unfixable. Taken as an excuse, it
would mean the device thread is exempt and the ring buffer write could do anything it liked.

Resolved: **our audio path begins where our code begins.** The boundary on the device thread is
the moment we take the buffer, and everything we do from there — conversion, the ring write — is
inside and is measured. What the library does before handing it over is a dependency's cost, and
it is the reason the WASAPI backend drives `IAudioCaptureClient` directly rather than using
NAudio's event wrappers, which raise an event argument object per callback.

Two consequences worth stating: a rule can be enforced on the code you own and not on the code
you don't, and where the third-party cost is unacceptable the answer is to stop using that part
of it, not to widen the boundary.

## A second one, shorter

**The recording tap** runs on the audio thread and is inside — it does exactly one ring buffer
write and, if the ring is full, increments a counter. The **writer thread** that drains that ring
into a file is outside, and may block on the disk for as long as the disk takes. That split is
the whole reason a failing disk cannot take a live broadcast down.

## What to measure in a test

Measure **a realistic process-a-buffer call**, not a synthetic loop. `AllocationAssert.None`
around a single arithmetic helper proves that helper allocates nothing and proves nothing about
the engine.

The canonical measured region is one full block through a graph with several channels, a full
modifier chain on each, the automixer on, several buses, and the recording taps and meters
enabled — because a channel count of one, or a bypassed chain, will not exercise the paths where
a closure or a `ToString` hides.

Measure it a second time **while the control thread is publishing snapshots**. A device name
formatted into a log message, or a lambda that captures, will pass the quiet test and fail the
busy one — and the busy one is what a live session looks like, because the operator is touching
the console.

Use the closure-free overload, the one taking a state argument and a `static` lambda. A capturing
lambda allocates its own closure, and then the harness is measuring itself.

## When you are still not sure

Ask whether the callback's next deadline can be missed because of what this code does. If it can,
it is inside. If the answer depends on how fast a disk, a socket or a garbage collector happens to
be that day, it is inside, and it needs to move.
