using System.Runtime.InteropServices;
using Vam.Engine.Dsp;

namespace Vam.Engine.Windows.Dsp;

/// <summary>
/// RNNoise, through the native library. B4.
/// </summary>
/// <remarks>
/// <para>
/// <b>It lives here rather than in the engine because of the P/Invoke.</b> <c>Vam.Engine</c> is
/// asserted platform-free by a test, and that test is right to object to
/// <c>System.Runtime.InteropServices</c> appearing in it — the day it stops objecting is the day
/// something genuinely platform-specific slips in behind it. The engine declares
/// <see cref="INoiseSuppressor"/>; this implements it.
/// </para>
/// <para>
/// <b>RNNoise works in 480-sample frames at 48 kHz and nothing else.</b> That is not a parameter, it
/// is what the network was trained on. The console runs 120-sample blocks, so this buffers four of
/// them, and the four blocks of delay that costs — ten milliseconds — are declared through
/// <see cref="LatencySamples"/> so the latency aligner compensates the other strips around it.
/// Without that declaration the automixer would compare a denoised strip against an undenoised one
/// ten milliseconds earlier and hand the gain to whichever finished first.
/// </para>
/// <para>
/// <b>It expects samples scaled to sixteen-bit range</b>, not to ±1. Feeding it floats in the range
/// the rest of the graph uses makes it decide the signal is silence and gate the whole thing, which
/// is a very confusing failure to debug — hence the scaling on the way in and out.
/// </para>
/// <para>
/// The library is not shipped with VAM. It is BSD-licensed and freely available, and dropping
/// <c>rnnoise.dll</c> beside the engine is the whole of the installation — see
/// <see cref="IsAvailable"/>, which is what the engine checks before choosing this over the managed
/// fallback.
/// </para>
/// </remarks>
public sealed class RnnoiseSuppressor : INoiseSuppressor, IDisposable
{
    /// <summary>The only frame size RNNoise has. Not a setting.</summary>
    public const int FrameSamples = 480;

    /// <summary>What RNNoise expects a full-scale sample to be.</summary>
    const float Scale = 32768f;

    readonly float[] input = new float[FrameSamples];
    readonly float[] output = new float[FrameSamples];

    nint state;
    int filled;
    bool isDisposed;

    /// <summary>Creates one denoiser state.</summary>
    /// <exception cref="DllNotFoundException">The native library is not beside the engine.</exception>
    public RnnoiseSuppressor()
    {
        state = Native.Create(nint.Zero);

        if (state == nint.Zero)
        {
            throw new InvalidOperationException("RNNoise refused to create a denoiser state.");
        }
    }

    /// <summary>
    /// Whether the native library is present and answering.
    /// </summary>
    /// <remarks>
    /// Asked once, at startup, and never on the audio thread. A first-time user without the library
    /// gets the managed suppressor and a line in the log saying which one is running, because a
    /// meeting has to start.
    /// </remarks>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                nint probe = Native.Create(nint.Zero);

                if (probe == nint.Zero)
                {
                    return false;
                }

                Native.Destroy(probe);

                return true;
            }
            catch (Exception failure)
                when (failure is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string Name => "RNNoise";

    /// <inheritdoc />
    /// <remarks>One RNNoise frame. Declared so the aligner can compensate the strips around it.</remarks>
    public int LatencySamples => FrameSamples;

    /// <inheritdoc />
    public void Process(Span<float> samples, float strength)
    {
        if (state == nint.Zero)
        {
            return;
        }

        for (int index = 0; index < samples.Length; index++)
        {
            input[filled++] = samples[index] * Scale;

            // The denoised sample handed back is the one from a frame ago, which is what the
            // declared latency is. Reading and writing the same position keeps the buffer to one
            // array and the whole thing to a single pass.
            samples[index] = Mix(samples[index], output[filled - 1] / Scale, strength);

            if (filled < FrameSamples)
            {
                continue;
            }

            filled = 0;

            RunFrame();
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(input);
        Array.Clear(output);

        filled = 0;

        if (state == nint.Zero)
        {
            return;
        }

        Native.Destroy(state);

        state = Native.Create(nint.Zero);
    }

    /// <summary>Returns the native state.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (state != nint.Zero)
        {
            Native.Destroy(state);
            state = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wet against dry, so an operator can back the denoise off rather than only switch it out.
    /// </summary>
    /// <remarks>
    /// B4 asks for a wet/dry control and not a switch, because RNNoise pushed hard makes speech
    /// sound underwater and the right amount is a judgement made by listening to the room.
    /// </remarks>
    static float Mix(float dry, float wet, float strength)
    {
        float amount = Math.Clamp(strength, 0f, 1f);

        return (dry * (1f - amount)) + (wet * amount);
    }

    void RunFrame()
    {
        // The whole reason this class exists, and the only line in it that is not bookkeeping.
        // Audio thread: no allocation, no lock, one call into native code that does neither.
        unsafe
        {
            fixed (float* into = output)
            fixed (float* from = input)
            {
                Native.ProcessFrame(state, into, from);
            }
        }
    }

    /// <summary>The three entry points RNNoise has that matter.</summary>
    static class Native
    {
        const string Library = "rnnoise";

        [DllImport(Library, EntryPoint = "rnnoise_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern nint Create(nint model);

        [DllImport(Library, EntryPoint = "rnnoise_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Destroy(nint state);

        /// <summary>Denoises one 480-sample frame in place, and returns its speech probability.</summary>
        [DllImport(Library, EntryPoint = "rnnoise_process_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe float ProcessFrame(nint state, float* output, float* input);
    }
}
