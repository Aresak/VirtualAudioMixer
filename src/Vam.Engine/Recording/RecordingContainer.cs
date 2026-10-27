namespace Vam.Engine.Recording;

/// <summary>The file a track is written as. Both are lossless twenty-four bit.</summary>
public enum RecordingContainer
{
    /// <summary>WAV, RF64 when it outgrows four gigabytes.</summary>
    Wave,

    /// <summary>FLAC.</summary>
    Flac
}
