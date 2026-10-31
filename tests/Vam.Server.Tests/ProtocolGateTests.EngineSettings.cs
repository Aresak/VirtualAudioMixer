using Microsoft.Extensions.Logging.Abstractions;
using Vam.Protocol.V1;
using Vam.Server.Engine;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests;

/// <summary>The recording and startup choices an operator makes survive the engine restarting. #136.</summary>
public partial class ProtocolGateTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public async Task RecordingAndStartupChoicesSurviveARestart()
    {
        string elsewhere = Path.Combine(workspace, "elsewhere");

        await ApplyAcceptedAsync(new Command
        {
            SetStartupOptions = new SetStartupOptions { LoadLastConsole = false, RecordAutomatically = false }
        });
        await ApplyAcceptedAsync(new Command
        {
            SetCaptureOptions = new SetCaptureOptions
            {
                Captures = new RecordingCapture { Inputs = false, StreamBus = true, AllBuses = true, Format = "flac24" }
            }
        });
        await ApplyAcceptedAsync(new Command { SetRecordingsPath = new SetRecordingsPath { Path = elsewhere } });

        engine!.Stop();

        // Started with the defaults, so every value below can only have come from what was saved.
        // Recording automatically is on by default, which is the one that matters most.
        using VamEngine restarted = new(
            new EngineOptions
            {
                ConsolePath = Path.Combine(workspace, "console.json"),
                RecordingDirectory = Path.Combine(workspace, "recordings"),
                ExpectedSessionDuration = TimeSpan.FromMinutes(1)
            },
            NullLoggerFactory.Instance,
            devices);

        Assert.Equal((false, false), restarted.Startup);
        Assert.Equal(
            new CaptureSelection { Inputs = false, StreamBus = true, AllBuses = true, Format = CaptureSelection.FlacFormat },
            restarted.Capture);
        Assert.Equal(elsewhere, restarted.Paths.Recordings);

        restarted.Start();

        Assert.Null(restarted.Recording);

        restarted.Stop();
    }

    async Task ApplyAcceptedAsync(Command command)
    {
        CommandReply reply = await client!.ApplyAsync(command, cancellationToken: Token);

        Assert.True(reply.Accepted, reply.Reason);
    }
}
