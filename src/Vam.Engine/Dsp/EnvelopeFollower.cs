namespace Vam.Engine.Dsp;

/// <summary>
/// Follows a signal's level, quickly upwards and slowly downwards.
/// </summary>
/// <remarks>
/// <para>
/// Asymmetric on purpose, and the asymmetry is what every dynamics processor in the engine is
/// actually made of. Rising fast means a compressor catches the start of a word rather than letting
/// it through; falling slowly means a gate does not chatter between syllables. One time constant
/// for both would force a choice between those two, and both matter.
/// </para>
/// <para>
/// Inside the audio path. One instance per channel.
/// </para>
/// </remarks>
public sealed class EnvelopeFollower
{
    float attackCoefficient = 1f;
    float releaseCoefficient = 1f;
    float envelope;

    /// <summary>The level it is currently following.</summary>
    public float Value => envelope;

    /// <summary>Sets how fast it moves. Control thread.</summary>
    /// <param name="attackSeconds">Time to travel most of the way up.</param>
    /// <param name="releaseSeconds">Time to travel most of the way down.</param>
    /// <param name="sampleRate">The rate it will run at.</param>
    public void SetTimes(double attackSeconds, double releaseSeconds, int sampleRate)
    {
        attackCoefficient = Coefficient(attackSeconds, sampleRate);
        releaseCoefficient = Coefficient(releaseSeconds, sampleRate);
    }

    /// <summary>Follows one sample's magnitude.</summary>
    /// <param name="magnitude">How loud, already rectified.</param>
    /// <returns>Where the envelope now is.</returns>
    public float Follow(float magnitude)
    {
        float coefficient = magnitude > envelope ? attackCoefficient : releaseCoefficient;

        envelope += (magnitude - envelope) * coefficient;

        return envelope;
    }

    /// <summary>Forgets the level it was following.</summary>
    public void Reset() => envelope = 0f;

    static float Coefficient(double seconds, int sampleRate)
    {
        // A time of zero means "immediately", which is a coefficient of one rather than a division
        // by zero. Instant attack is a legitimate setting for a limiter.
        if (seconds <= 0.0)
        {
            return 1f;
        }

        return (float)(1.0 - Math.Exp(-1.0 / (seconds * sampleRate)));
    }
}
