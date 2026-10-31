using Microsoft.Extensions.Logging.Abstractions;
using Vam.Server.Engine;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Engine;

/// <summary>What happens to saved engine settings that cannot be used. #136.</summary>
public sealed class EngineSettingsStoreTests : IDisposable
{
    readonly string workspace = Path.Combine(Path.GetTempPath(), "vam-settings-" + Guid.NewGuid().ToString("n"));

    string SettingsPath => Path.Combine(workspace, "engine.json");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WithNothingSavedTheOptionsAreLeftAlone()
    {
        EngineOptions options = Defaults();

        Store().Restore(options);

        Assert.True(options.RecordAutomatically);
        Assert.Equal(Path.Combine(workspace, "recordings"), options.RecordingDirectory);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ARelativeFolderAndAnUnknownFormatFallBackToTheDefaults()
    {
        Directory.CreateDirectory(workspace);
        File.WriteAllText(
            SettingsPath,
            """
            {
              "RecordAutomatically": false,
              "LoadLastConsole": true,
              "Capture": { "Inputs": false, "StreamBus": true, "AllBuses": false, "Format": "mp3" },
              "RecordingDirectory": "recordings-here"
            }
            """);
        EngineOptions options = Defaults();

        Store().Restore(options);

        // What can be used is still used. One bad value is not a reason to forget the others.
        Assert.False(options.RecordAutomatically);
        Assert.Equal(new CaptureSelection(), options.Capture);
        Assert.Equal(Path.Combine(workspace, "recordings"), options.RecordingDirectory);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void AFileThatCannotBeReadLeavesTheDefaults()
    {
        Directory.CreateDirectory(workspace);
        File.WriteAllText(SettingsPath, "{ not json");
        EngineOptions options = Defaults();

        Store().Restore(options);

        Assert.True(options.RecordAutomatically);
        Assert.True(options.LoadLastConsole);
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void WhatIsSavedIsWhatIsRestored()
    {
        EngineOptions changed = Defaults();
        changed.RecordAutomatically = false;
        changed.LoadLastConsole = false;
        changed.Capture = new CaptureSelection { AllBuses = true, Format = CaptureSelection.FlacFormat };
        changed.RecordingDirectory = Path.Combine(workspace, "elsewhere");

        Store().Save(changed);

        EngineOptions restored = Defaults();
        Store().Restore(restored);

        Assert.False(restored.RecordAutomatically);
        Assert.False(restored.LoadLastConsole);
        Assert.Equal(changed.Capture, restored.Capture);
        Assert.Equal(changed.RecordingDirectory, restored.RecordingDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    EngineSettingsStore Store() => new(SettingsPath, NullLogger<EngineSettingsStore>.Instance);

    EngineOptions Defaults() => new()
    {
        ConsolePath = Path.Combine(workspace, "console.json"),
        RecordingDirectory = Path.Combine(workspace, "recordings")
    };
}
