using Vam.Engine.Dsp;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Reads every strip and decides who is speaking. B3.
/// </summary>
/// <remarks>
/// <para>
/// <b>A tap, not a link.</b> It reads the signal and changes nothing, which is why it is a node in
/// its own right rather than a modifier in the chain — a modifier that returned its input unaltered
/// would be a lie about what the chain is for, and an operator could move it or switch it out.
/// </para>
/// <para>
/// <b>Placed before the denoise, deliberately.</b> A detector looking at denoised audio agrees with
/// the denoise rather than checking it: the denoise removes exactly the characteristics the detector
/// keys on, so a voice the denoise mistook for noise would read as silence here too and nothing
/// would ever notice. Sitting at the head means it sees what the microphone sent.
/// </para>
/// <para>
/// Inside the audio path. Its buffers are allocated when the plan is compiled, and observing a block
/// is two biquads and a pass over one plane.
/// </para>
/// </remarks>
public sealed class VoiceActivityTapNode : AudioNode
{
    readonly GraphLayout layout;
    readonly VoiceActivityDetector[] detectors;
    readonly bool[] speaking;
    readonly float[] levelsDb;

    /// <summary>Builds one detector per strip.</summary>
    /// <param name="layout">Where the planes are.</param>
    /// <param name="channelCount">How many strips.</param>
    /// <param name="sampleRate">The rate audio arrives at.</param>
    /// <param name="blockFrames">Largest block it will see.</param>
    public VoiceActivityTapNode(GraphLayout layout, int channelCount, int sampleRate, int blockFrames)
    {
        ArgumentNullException.ThrowIfNull(layout);

        this.layout = layout;

        detectors = new VoiceActivityDetector[channelCount];
        speaking = new bool[channelCount];
        levelsDb = new float[channelCount];

        for (int channel = 0; channel < channelCount; channel++)
        {
            detectors[channel] = new VoiceActivityDetector(sampleRate, blockFrames);
        }
    }

    /// <summary>Who is speaking, as of the last block. F2.</summary>
    public ReadOnlySpan<bool> Speaking => speaking;

    /// <summary>What each detector last measured, for the diagnostics view.</summary>
    public ReadOnlySpan<float> LevelsDb => levelsDb;

    /// <summary>How far each strip is above its own noise floor, from zero to one.</summary>
    /// <remarks>
    /// Used to bias the automixer's detector towards strips that carry speech rather than a chair
    /// scraping. A room where the loudest microphone is the one nearest the air conditioning is
    /// exactly the room gain sharing gets wrong without it.
    /// </remarks>
    /// <param name="channelIndex">Which strip.</param>
    /// <returns>Zero when the strip is at its own floor, one when it is well above it.</returns>
    public float ConfidenceOf(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= detectors.Length)
        {
            return 1f;
        }

        VoiceActivityDetector detector = detectors[channelIndex];
        float above = detector.LevelDb - detector.NoiseFloorDb;

        // Twelve decibels above the floor is as confident as this gets. Beyond that the strip is
        // plainly carrying a voice and a bigger number would only make a loud speaker louder.
        return Math.Clamp(above / 12f, 0f, 1f);
    }

    /// <inheritdoc />
    public override void Reset()
    {
        Array.Clear(speaking);
        Array.Clear(levelsDb);

        foreach (VoiceActivityDetector detector in detectors)
        {
            detector.Reset();
        }
    }

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        GraphSnapshot snapshot = context.Snapshot;

        for (int channel = 0; channel < detectors.Length && channel < snapshot.ChannelCount; channel++)
        {
            if (snapshot.Channels[channel].IsSilent)
            {
                // A muted or faulted strip is not speaking. Detecting on it would light a speaking
                // indicator for a microphone nothing can hear, which is worse than not lighting one.
                speaking[channel] = false;
                continue;
            }

            // The first plane only. Speech is speech on either side of a stereo pair, and running
            // the detector twice would double its cost for an answer that cannot differ usefully.
            speaking[channel] = detectors[channel].Observe(context.Plane(layout.PreFaderPlane(channel)));
            levelsDb[channel] = detectors[channel].LevelDb;
        }
    }
}
