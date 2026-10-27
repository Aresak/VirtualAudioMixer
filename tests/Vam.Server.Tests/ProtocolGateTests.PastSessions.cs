using Grpc.Core;
using Vam.Protocol.V1;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>Deleting, renaming and exporting past sessions over the protocol. #99.</summary>
public partial class ProtocolGateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task APastSessionCanBeDeleted()
    {
        PastSession session = await RecordOneSessionAsync();

        CommandReply reply = await client!.DeletePastSessionAsync(
            new PastSessionTarget { Directory = session.Directory },
            cancellationToken: Token);

        Assert.True(reply.Accepted, reply.Reason);
        Assert.False(Directory.Exists(session.Directory));

        PastSessionList after = await client.ListPastSessionsAsync(new Empty(), cancellationToken: Token);

        Assert.DoesNotContain(after.Sessions, listed => listed.Directory == session.Directory);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task APastSessionCanBeRenamed()
    {
        PastSession session = await RecordOneSessionAsync();

        CommandReply reply = await client!.RenamePastSessionAsync(
            new PastSessionRename { Directory = session.Directory, Name = "Council 27 October" },
            cancellationToken: Token);

        Assert.True(reply.Accepted, reply.Reason);
        Assert.False(Directory.Exists(session.Directory));

        PastSessionList after = await client.ListPastSessionsAsync(new Empty(), cancellationToken: Token);
        PastSession renamed = Assert.Single(after.Sessions, listed => Path.GetFileName(listed.Directory) == "Council 27 October");

        Assert.Equal(session.Tracks, renamed.Tracks);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task TheSessionBeingRecordedCannotBeDeletedRenamedOrExported()
    {
        CommandReply started = await client!.ApplyAsync(
            new Command { SetRecording = new SetRecording { Recording = true } },
            cancellationToken: Token);

        Assert.True(started.Accepted, started.Reason);

        string recording = (await client.GetConsoleAsync(new Empty(), cancellationToken: Token)).Recording.Directory;

        Assert.NotEmpty(recording);

        CommandReply deleted = await client.DeletePastSessionAsync(
            new PastSessionTarget { Directory = recording },
            cancellationToken: Token);
        CommandReply renamed = await client.RenamePastSessionAsync(
            new PastSessionRename { Directory = recording, Name = "elsewhere" },
            cancellationToken: Token);
        RpcException exported = await Assert.ThrowsAsync<RpcException>(() => ExportAsync(recording));

        Assert.False(deleted.Accepted);
        Assert.Contains("being recorded", deleted.Reason, StringComparison.Ordinal);
        Assert.False(renamed.Accepted);
        Assert.Contains("being recorded", renamed.Reason, StringComparison.Ordinal);
        Assert.Equal(StatusCode.FailedPrecondition, exported.StatusCode);
        Assert.True(Directory.Exists(recording));

        await client.ApplyAsync(new Command { SetRecording = new SetRecording { Recording = false } }, cancellationToken: Token);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AFolderThatIsNotAPastSessionIsRefused()
    {
        string stranger = Path.Combine(workspace, "not-a-session");

        Directory.CreateDirectory(stranger);
        await File.WriteAllTextAsync(Path.Combine(stranger, "keep.txt"), "mine", Token);

        foreach (string directory in (string[])[stranger, workspace, Path.Combine(workspace, "recordings"), "relative", string.Empty])
        {
            CommandReply reply = await client!.DeletePastSessionAsync(
                new PastSessionTarget { Directory = directory },
                cancellationToken: Token);

            Assert.False(reply.Accepted, directory);
        }

        Assert.True(File.Exists(Path.Combine(stranger, "keep.txt")));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task ARenameThatIsAPathOrTakenIsRefused()
    {
        PastSession session = await RecordOneSessionAsync();

        Directory.CreateDirectory(Path.Combine(workspace, "recordings", "taken"));

        foreach (string name in (string[])["", "  ", "..", @"..\escaped", "a/b", "taken"])
        {
            CommandReply reply = await client!.RenamePastSessionAsync(
                new PastSessionRename { Directory = session.Directory, Name = name },
                cancellationToken: Token);

            Assert.False(reply.Accepted, name);
        }

        Assert.True(Directory.Exists(session.Directory));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task AnExportCarriesEveryFileWhole()
    {
        PastSession session = await RecordOneSessionAsync();

        Dictionary<string, long> exported = await ExportAsync(session.Directory);
        Dictionary<string, long> onDisk = Directory
            .GetFiles(session.Directory, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(session.Directory, file).Replace('\\', '/'), file => new FileInfo(file).Length);

        Assert.Equal(onDisk.OrderBy(pair => pair.Key), exported.OrderBy(pair => pair.Key));
    }

    async Task<PastSession> RecordOneSessionAsync()
    {
        CommandReply started = await client!.ApplyAsync(
            new Command { SetRecording = new SetRecording { Recording = true } },
            cancellationToken: Token);

        Assert.True(started.Accepted, started.Reason);

        CommandReply stopped = await client.ApplyAsync(
            new Command { SetRecording = new SetRecording { Recording = false } },
            cancellationToken: Token);

        Assert.True(stopped.Accepted, stopped.Reason);

        PastSessionList list = await client.ListPastSessionsAsync(new Empty(), cancellationToken: Token);

        return list.Sessions[0];
    }

    async Task<Dictionary<string, long>> ExportAsync(string directory)
    {
        Dictionary<string, long> files = [];

        using AsyncServerStreamingCall<PastSessionChunk> call = client!.ExportPastSession(
            new PastSessionTarget { Directory = directory },
            cancellationToken: Token);

        await foreach (PastSessionChunk chunk in call.ResponseStream.ReadAllAsync(Token))
        {
            files[chunk.File] = files.GetValueOrDefault(chunk.File) + chunk.Data.Length;
        }

        return files;
    }
}
