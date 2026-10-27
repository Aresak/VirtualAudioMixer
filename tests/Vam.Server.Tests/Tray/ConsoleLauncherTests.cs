using Vam.Server.Tray;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Tray;

/// <summary>#60. Where Open console looks for the console.</summary>
public sealed class ConsoleLauncherTests : IDisposable
{
    readonly DirectoryInfo root = Directory.CreateTempSubdirectory("vam-tray-");

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void BesideTheEngineWins()
    {
        string engine = Folder("release");
        string console = Touch(Path.Combine(engine, "Vam.Client.exe"));

        Assert.Equal(console, ConsoleLauncher.Locate(engine));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void ASourceCheckoutFindsTheNewestBuild()
    {
        Touch(Path.Combine(root.FullName, "VirtualAudioMixer.sln"));
        string engine = Folder("src", "Vam.Server", "bin", "Debug", "net10.0-windows");
        string older = Touch(Path.Combine(Folder("src", "Vam.Client", "bin", "Release", "x"), "Vam.Client.exe"));
        string newer = Touch(Path.Combine(Folder("src", "Vam.Client", "bin", "Debug", "x"), "Vam.Client.exe"));

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(newer, ConsoleLauncher.Locate(engine));
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NowhereIsNull()
    {
        Touch(Path.Combine(root.FullName, "VirtualAudioMixer.sln"));

        Assert.Null(ConsoleLauncher.Locate(Folder("src", "Vam.Server", "bin")));
    }

    public void Dispose() => root.Delete(recursive: true);

    string Folder(params string[] parts) =>
        Directory.CreateDirectory(Path.Combine([root.FullName, .. parts])).FullName;

    static string Touch(string path)
    {
        File.WriteAllBytes(path, []);

        return path;
    }
}
