using Microsoft.Extensions.Configuration;
using Vam.Server.Tray;
using Vam.TestKit.Harness;
using Xunit;

namespace Vam.Server.Tests.Tray;

/// <summary>#60. Vam:Tray, on by default.</summary>
public class TrayConfigurationTests
{
    [Theory]
    [Trait("Category", TestCategories.Unit)]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("nonsense", true)]
    public void OnUnlessItSaysFalse(string? value, bool expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TrayConfigurationExtensions.Key] = value })
            .Build();

        Assert.Equal(expected, configuration.WantsTray());
    }
}
