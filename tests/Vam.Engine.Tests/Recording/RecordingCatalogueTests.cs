using Microsoft.Extensions.Logging.Abstractions;
using Vam.Engine.Recording;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Recording;

/// <summary>
/// E3. What the console can say about sessions that are already on disk.
/// </summary>
public class RecordingCatalogueTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "vam-catalogue-" + Guid.NewGuid().ToString("n"));

    readonly RecordingCatalogue catalogue = new(NullLogger<RecordingCatalogue>.Instance);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void SessionsComeBackNewestFirstWithWhatTheFolderSays()
    {
        WriteSession("2026-05-27_19-00-00", tracks: 2, bytesEach: 1000);
        WriteSession("2026-08-28_18-30-00", tracks: 6, bytesEach: 2000);

        IReadOnlyList<RecordedSession> sessions = catalogue.Read(root);

        Assert.Equal(2, sessions.Count);

        // Newest first, because the question after a meeting is about the meeting that just
        // happened.
        Assert.Equal(6, sessions[0].Tracks);
        Assert.Equal(12000, sessions[0].Bytes);
        Assert.Equal(new DateTime(2026, 8, 28, 18, 30, 0), sessions[0].StartedAt.DateTime);

        // Nothing wrote a manifest, so the two figures the files cannot be asked for are absent
        // rather than guessed at.
        Assert.Null(sessions[0].Duration);
        Assert.Null(sessions[0].DroppedFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AManifestSuppliesWhatTheFilesCannotBeAskedFor()
    {
        string directory = WriteSession("2026-08-28_18-30-00", tracks: 3, bytesEach: 100);

        File.WriteAllText(
            Path.Combine(directory, SessionManifest.FileName),
            """{"StartedAt":"2026-08-28T18:30:00+00:00","DurationSeconds":6432,"Tracks":3,"DroppedFrames":17}""");

        RecordedSession session = Assert.Single(catalogue.Read(root));

        Assert.Equal(TimeSpan.FromSeconds(6432), session.Duration);
        Assert.Equal(17, session.DroppedFrames);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFolderWithNoAudioInItIsNotASession()
    {
        // A recording the disk guard refused leaves one of these behind, and listing it would be
        // listing a meeting nobody has.
        Directory.CreateDirectory(Path.Combine(root, "2026-08-28_18-30-00"));

        Assert.Empty(catalogue.Read(root));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARootThatIsNotThereIsAnEmptyListAndNotAFailure()
    {
        // The rest of the recording view has to keep working. An operator about to start a meeting
        // does not care that last month's folder is on a disk nobody has plugged in.
        Assert.Empty(catalogue.Read(Path.Combine(root, "nowhere")));
        Assert.Empty(catalogue.Read(string.Empty));
    }

    string WriteSession(string name, int tracks, int bytesEach)
    {
        string directory = Path.Combine(root, name);

        Directory.CreateDirectory(directory);

        for (int track = 0; track < tracks; track++)
        {
            File.WriteAllBytes(Path.Combine(directory, $"track-{track}.wav"), new byte[bytesEach]);
        }

        return directory;
    }
}
