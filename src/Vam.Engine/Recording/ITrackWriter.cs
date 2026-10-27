namespace Vam.Engine.Recording;

/// <summary>Writes one recorded track to a file. Writer thread only.</summary>
public interface ITrackWriter : IDisposable
{
    /// <summary>Where the file is.</summary>
    string Path { get; }

    /// <summary>Frames written so far.</summary>
    long FrameCount { get; }

    /// <summary>Bytes the file holds so far, headers included or not as the format has them.</summary>
    long BytesWritten { get; }

    /// <summary>Writes one block of interleaved samples.</summary>
    /// <param name="samples">The audio, interleaved at the declared channel count.</param>
    void Write(ReadOnlySpan<float> samples);

    /// <summary>Writes what is pending, patches the header and flushes. Safe to call twice.</summary>
    void Finish();
}
