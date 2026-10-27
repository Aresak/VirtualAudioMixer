using System.IO.Compression;
using Google.Protobuf;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Vam.Ui.Extensions;
using Xunit;

namespace Vam.Ui.Tests.Extensions;

/// <summary>A past session's name, and an export put back into one archive. #99.</summary>
public class PastSessionExtensionsTests
{
    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(@"C:\Recordings\2026-10-27_18-00-00", "2026-10-27_18-00-00")]
    [InlineData(@"C:\Recordings\Council\", "Council")]
    [InlineData("/srv/recordings/Council", "Council")]
    [InlineData("Council", "Council")]
    public void TheFolderNameIsTheLastPartOfEitherKindOfPath(string directory, string expected)
    {
        Assert.Equal(expected, directory.FolderName());
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ChunksBecomeWholeFilesUnderTheSessionFolder()
    {
        PastSessionChunk[] chunks =
        [
            Chunk("Mayor.wav", [1, 2, 3]),
            Chunk("Mayor.wav", [4, 5]),
            Chunk("session.json", [7]),
            Chunk("empty.txt", [])
        ];

        using MemoryStream archive = new();

        await chunks.ToAsyncEnumerable().WriteZipAsync("Council", archive, TestContext.Current.CancellationToken);

        archive.Position = 0;

        using ZipArchive read = new(archive, ZipArchiveMode.Read);

        Assert.Equal(["Council/Mayor.wav", "Council/session.json", "Council/empty.txt"], read.Entries.Select(entry => entry.FullName));
        Assert.Equal(5, read.GetEntry("Council/Mayor.wav")!.Length);
        Assert.Equal(0, read.GetEntry("Council/empty.txt")!.Length);
    }

    static PastSessionChunk Chunk(string file, byte[] data) =>
        new() { File = file, Data = ByteString.CopyFrom(data) };
}
