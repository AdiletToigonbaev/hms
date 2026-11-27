using Hms.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Hms.ApiTests;

public class AppVersionTests
{
    private static IConfiguration Config(string? version) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:Version"] = version }).Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotSet_IsDev(string? value) => AppVersion.From(Config(value)).Should().Be("dev");

    [Fact]
    public void FromEnvironment_IsReleaseTag() => AppVersion.From(Config(" v1.2.0 ")).Should().Be("v1.2.0");
}
