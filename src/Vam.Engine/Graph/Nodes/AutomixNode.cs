using Vam.Engine.Automix;
using Vam.Engine.Dsp.Extensions;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Gain sharing across every participating microphone. EPIC-06.
/// </summary>
/// <remarks>
/// <para>
/// <b>One node, not one per channel.</b> Gain sharing is a normalisation across the whole console:
/// no strip's gain can be worked out until every strip's level is known, so splitting this per
/// channel would mean each of them reading the others.
/// </para>
/// <para>
/// It sits after the fader on purpose. The fader is the tail anchor and keeps meaning what it says —
/// the operator sets the balance, the automixer decides who is currently being heard, and those are
/// different jobs that would fight if they were in the other order.
/// </para>
/// <para>
/// <b>Smoothed in the decibel domain with asymmetric coefficients, and that is the single most
/// audible parameter in the feature.</b> Opening in about fifteen milliseconds means no clipped
/// first syllable; closing over the response time means no chatter between words. One knob sets the
/// release and the attack is derived from it, because an operator has one question — how quickly
/// should this follow the conversation — and two controls to set against each other is two chances
/// to get it wrong.
/// </para>
/// </remarks>
public sealed class AutomixNode(GraphLayout layout, AutomixState state, int sampleRate, int blockFrames) : AudioNode
{
    /// <summary>
    /// The exponent the detector is raised to.
    /// </summary>
    /// <remarks>
    /// Above one, so a microphone that is clearly louder takes clearly more of the gain rather than
    /// its proportional share. At exactly one, two microphones a few decibels apart end up almost
    /// equal and the automixer barely does anything; much above two it becomes a switch.
    /// </remarks>
    const double DetectorExponent = 2.2;

    /// <summary>How much faster opening is than closing.</summary>
    const float AttackDivisor = 8f;

    const float MinimumLevelDb = -100f;

    readonly float[] detectors = new float[layout.ChannelCount];
    readonly float[] gainsDb = new float[layout.ChannelCount];

    /// <summary>What the automixer is doing, for the console.</summary>
    public AutomixState State => state;

    /// <inheritdoc />
    public override void Reset()
    {
        Array.Clear(detectors);
        Array.Clear(gainsDb);

        state.Reset();
    }

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        AutomixParams parameters = context.Snapshot.Automix;

        if (parameters.IsBypassed)
        {
            // C10. One branch at the top writing unity, reachable from any view, and the state says
            // so plainly rather than leaving stale shares on the console.
            ClearToUnity();
            return;
        }

        float total = Detect(ref context, parameters);

        Apply(ref context, parameters, total);
    }

    static float ToGain(float decibels) => decibels <= MinimumLevelDb ? 0f : (float)Math.Pow(10.0, decibels / 20.0);

    void ClearToUnity()
    {
        for (int channel = 0; channel < gainsDb.Length; channel++)
        {
            gainsDb[channel] = 0f;
            state.Record(channel, 0f, 0f);
        }

        state.RecordOpenMicrophones(0f);
    }

    float Detect(ref RenderContext context, AutomixParams parameters)
    {
        GraphSnapshot snapshot = context.Snapshot;
        float total = 0f;

        for (int channel = 0; channel < detectors.Length; channel++)
        {
            detectors[channel] = 0f;

            if (channel >= parameters.Channels.Length || !parameters.Channels[channel].Participates)
            {
                continue;
            }

            if (channel >= snapshot.ChannelCount || snapshot.Channels[channel].IsSilent)
            {
                continue;
            }

            int width = layout.ChannelWidth(channel);
            int first = layout.PostFaderPlane(channel);
            float level = 0f;

            for (int plane = 0; plane < width; plane++)
            {
                level = Math.Max(level, ((ReadOnlySpan<float>)context.Plane(first + plane)).PeakAbs());
            }

            detectors[channel] = (float)Math.Pow(level * parameters.Channels[channel].Weight, DetectorExponent);
            total += detectors[channel];
        }

        return total;
    }

    void Apply(ref RenderContext context, AutomixParams parameters, float total)
    {
        float attack = Coefficient(parameters.ResponseMilliseconds / AttackDivisor);
        float release = Coefficient(parameters.ResponseMilliseconds);
        float sumOfSquares = 0f;

        for (int channel = 0; channel < detectors.Length; channel++)
        {
            // A strip that is not part of the sharing is not part of the sharing. Applying the depth
            // to it would turn the online return down every time nobody in the room was speaking,
            // which is precisely backwards - that is when the remote voices matter most.
            if (channel >= parameters.Channels.Length || !parameters.Channels[channel].Participates)
            {
                gainsDb[channel] = 0f;
                state.Record(channel, 0f, 0f);

                continue;
            }

            float share = total > 0f ? detectors[channel] / total : 0f;

            sumOfSquares += share * share;

            float targetDb = share > 0f
                ? Math.Max(parameters.DepthDb, (float)(20.0 * Math.Log10(share)))
                : parameters.DepthDb;

            // Rising uses the fast coefficient, falling the slow one. Getting this the wrong way
            // round clips the first syllable of every sentence, which is the complaint that gets an
            // automixer switched off.
            float coefficient = targetDb > gainsDb[channel] ? attack : release;

            gainsDb[channel] += (targetDb - gainsDb[channel]) * coefficient;

            state.Record(channel, share, gainsDb[channel]);
            Scale(ref context, channel, ToGain(gainsDb[channel]));
        }

        state.RecordOpenMicrophones(sumOfSquares);
    }

    void Scale(ref RenderContext context, int channel, float gain)
    {
        int width = layout.ChannelWidth(channel);
        int first = layout.PostFaderPlane(channel);

        for (int plane = 0; plane < width; plane++)
        {
            context.Plane(first + plane).Scale(gain);
        }
    }

    float Coefficient(float milliseconds)
    {
        double seconds = Math.Max(milliseconds, 1f) * 0.001;

        return (float)(1.0 - Math.Exp(-(double)blockFrames / (seconds * sampleRate)));
    }
}
