using System.Reflection;
using Vam.Engine.Devices;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Engine.Tests.Devices;

/// <summary>
/// Guards the split that makes everything else testable: `Vam.Engine` knows no platform.
/// </summary>
/// <remarks>
/// The Windows device layer lives in its own assembly because CA1416 forced it, but the reason it
/// stays there is this: the mix graph, every modifier, the automixer, the recorder and the soak
/// driver all run on any machine with no audio hardware. One convenient `using NAudio` in the
/// engine would take that away, and nothing else would notice until CI moved or somebody tried to
/// run a soak on a laptop with nothing plugged in.
/// </remarks>
public class EnginePlatformIndependenceTests
{
    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void TheEngineReferencesNoAudioLibraryAndNoPlatformAssembly()
    {
        Assembly engine = typeof(AudioRingBuffer).Assembly;

        string[] forbidden = ["NAudio", "Windows", "Interop"];

        foreach (AssemblyName reference in engine.GetReferencedAssemblies())
        {
            foreach (string fragment in forbidden)
            {
                Assert.False(
                    reference.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) ?? false,
                    $"Vam.Engine references {reference.Name}. The engine is platform-free by design - "
                    + "device code belongs in Vam.Engine.Windows.");
            }
        }
    }

    [Fact]
    [Trait("Category", TestCategories.Unit)]
    public void NoEngineTypeIsMarkedAsPlatformSpecific()
    {
        Assembly engine = typeof(AudioRingBuffer).Assembly;

        // A type that needed a [SupportedOSPlatform] attribute to compile is a type that has already
        // reached for a platform API. Catching it here is cheaper than catching it when someone
        // tries to build for macOS in EPIC-18.
        foreach (Type type in engine.GetTypes())
        {
            Assert.DoesNotContain(
                type.GetCustomAttributes(inherit: false),
                attribute => attribute.GetType().Name.Contains("SupportedOSPlatform", StringComparison.Ordinal));
        }
    }
}
