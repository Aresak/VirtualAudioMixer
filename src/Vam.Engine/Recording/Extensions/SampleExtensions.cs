namespace Vam.Engine.Recording.Extensions;

/// <summary>The one conversion every track writer shares, so WAV and FLAC hold the same integers.</summary>
public static class SampleExtensions
{
    /// <summary>Full scale for a signed twenty-four bit sample.</summary>
    public const float Pcm24FullScale = 8388607f;

    /// <summary>Quantises a sample to signed twenty-four bit, clipping at full scale.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>The integer written to disk.</returns>
    public static int ToPcm24(this float sample) =>
        (int)Math.Clamp(sample * Pcm24FullScale, -Pcm24FullScale, Pcm24FullScale);
}
