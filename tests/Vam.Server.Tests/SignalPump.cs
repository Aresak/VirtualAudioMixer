using System.Diagnostics;
using Vam.TestKit.Devices;

namespace Vam.Server.Tests;

/// <summary>
/// Plays the null devices in real time, so an engine on a <see cref="NullAudioBackend"/> has a
/// signal to render and a clock asking for it.
/// </summary>
/// <remarks>
/// Captures and renders are pumped by the same elapsed time on one thread, so every ring has one
/// producer and one consumer and the two rates agree exactly.
/// </remarks>
sealed class SignalPump : IDisposable
{
    readonly NullAudioBackend devices;
    readonly CancellationTokenSource stopping = new();
    readonly Thread thread;

    public SignalPump(NullAudioBackend devices)
    {
        this.devices = devices;

        thread = new Thread(Run) { IsBackground = true, Name = "test-signal-pump" };
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
}
