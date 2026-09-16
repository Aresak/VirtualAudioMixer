using Vam.Engine.Recording;

namespace Vam.Engine.Graph.Nodes;

/// <summary>
/// Takes a copy of a strip or a bus on its way past, for the recording. E3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placed after the trim and before everything else.</b> The multitrack is the raw material, and
/// raw means before the gate decided something was silence and before the denoise decided something
/// was noise. If the automixer misbehaves during a meeting, this is what the stream gets rebuilt
/// from — and it is only worth anything if the processing has not already happened to it.
/// </para>
/// <para>
/// Inside the audio path, and its entire contribution is one ring write. A full ring drops the block
/// and counts it, because a failing disk must not be able to stop a live broadcast.
/// </para>
/// </remarks>
public sealed class RecordingTapNode : AudioNode
{
    readonly RecordingTrack track;
    readonly float[] interleaved;
    readonly int firstPlane;
    readonly int width;

    /// <summary>Taps a run of planes into a track.</summary>
    /// <param name="track">Where the copy goes.</param>
    /// <param name="firstPlane">First plane to take.</param>
    /// <param name="width">How many.</param>
    /// <param name="blockFrames">Frames per block, to size the scratch once.</param>
    public RecordingTapNode(RecordingTrack track, int firstPlane, int width, int blockFrames)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockFrames, 1);

        this.track = track;
        this.firstPlane = firstPlane;
        this.width = width;

        interleaved = new float[width * blockFrames];
    }

    /// <summary>The track this tap feeds.</summary>
    public RecordingTrack Track => track;

    /// <inheritdoc />
    public override void Process(ref RenderContext context)
    {
        int frames = Math.Min(context.FrameCount, interleaved.Length / width);

        for (int channel = 0; channel < width; channel++)
        {
            ReadOnlySpan<float> plane = context.Plane(firstPlane + channel);

            for (int frame = 0; frame < frames; frame++)
            {
                interleaved[(frame * width) + channel] = plane[frame];
            }
        }

        track.Capture(interleaved.AsSpan(0, frames * width), frames);
    }
}
