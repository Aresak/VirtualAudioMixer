using System.Diagnostics;
using Vam.TestKit.Devices;

namespace Vam.Server.Tests;

/// <summary>
/// Plays the null devices in real time, so an engine on a <see cref="NullAudioBackend"/> has a
/// signal to render and a clock asking for it.
/// </summary>
/// <remarks>
/// <para>
/// Captures and renders are pumped by the same elapsed time on one thread, so every ring has one
/// producer and one consumer and the two rates agree exactly.
/// </para>
/// <para>
/// They agree slice by slice as well as overall. The render callback runs the whole graph on this
/// thread, so on a machine running several test suites at once one pass can take longer than a
/// capture ring holds. Delivered as one burst, the capture for that time overflowed the ring and
/// the render that followed emptied it, and a strip read as silence for the rest of the test
/// (#141). A real device delivers a buffer and a buffer is played, in turn, however late both are.
/// </para>
/// </remarks>
sealed class SignalPump : IDisposable
{
    /// <summary>How much time is delivered and played before the next slice: one 120-frame block.</summary>
    static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(2.5);

    readonly NullAudioBackend devices;
    readonly CancellationTokenSource stopping = new();
    readonly Thread thread;

    public SignalPump(NullAudioBackend devices)
    {
        this.devices = devices;

        // Above the test threads, as a device's own audio thread is above an application's.
        thread = new Thread(Run) { IsBackground = true, Name = "test-signal-pump", Priority = ThreadPriority.Highest };
        thread.Start();
    }

    public void Dispose()
    {
        stopping.Cancel();

        bool stopped = thread.Join(TimeSpan.FromSeconds(5));

        stopping.Dispose();

        // A render callback that never returns is a hung audio thread, and should fail as one.
        if (!stopped)
        {
            throw new TimeoutException("The signal pump is stuck inside a device callback.");
        }
    }

    void Run()
    {
        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan last = TimeSpan.Zero;

        while (!stopping.IsCancellationRequested)
        {
            Thread.Sleep(1);

            TimeSpan now = clock.Elapsed;
            TimeSpan elapsed = now - last;

            last = now;

            while (elapsed > TimeSpan.Zero)
            {
                TimeSpan step = elapsed < Slice ? elapsed : Slice;

                Pump(step);
                elapsed -= step;
            }
        }
    }

    void Pump(TimeSpan elapsed)
    {
        // Streams the engine has closed ignore the pump, so all of them are pumped.
        foreach (NullCaptureStream capture in devices.CaptureStreams)
        {
            capture.Pump(elapsed);
        }

        foreach (NullRenderStream render in devices.RenderStreams)
        {
            render.Pump(elapsed);
        }
    }
}
