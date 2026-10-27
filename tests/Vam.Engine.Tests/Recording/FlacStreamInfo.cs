namespace Vam.Engine.Tests.Recording;

/// <summary>The STREAMINFO fields of a FLAC file, as the test decoder read them.</summary>
sealed record FlacStreamInfo(
    int MinBlockSize,
    int MaxBlockSize,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalSamples,
    byte[] Md5);
