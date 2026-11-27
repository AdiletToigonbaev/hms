using Hms.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Hms.ApiTests;

public class ProductionConfigurationCheckTests
{
    private static readonly string StrongKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Jwt:SigningKey"] = StrongKey,
        ["ConnectionStrings:Hms"] = "Host=db;Database=hms;Username=hms_app;Password=" + Guid.NewGuid().ToString("N"),
        ["Bootstrap:Owner:Password"] = Guid.NewGuid().ToString("N"),
        ["Identity:PublicWebUrl"] = "https://hotel.example.com",
    };

    [Fact]
    public void ValidProductionConfiguration_HasNoErrors()
    {
        ProductionConfigurationCheck.Validate(Config(Valid())).Should().BeEmpty();
    }

    [Fact]
    public void DevelopmentDefaults_AreAllRejected()
    {
        // Значения из appsettings.Development.json и deploy/docker-compose.yml.
        var dev = Config(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "dev-only-signing-key-do-not-use-in-production-0123456789",
            ["ConnectionStrings:Hms"] = "Host=db;Port=5432;Database=hms;Username=hms;Password=hms_dev_only",
            ["Bootstrap:Owner:Password"] = "owner-dev-password",
            ["Identity:PublicWebUrl"] = "http://localhost:3000",
        });

        ProductionConfigurationCheck.Validate(dev).Should().HaveCount(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hotel.example.com")]
    [InlineData("http://hotel.example.com")]
    public void PublicWebUrl_WithoutHttps_IsRejected(string? url)
    {
        var values = Valid();
        values["Identity:PublicWebUrl"] = url;

        ProductionConfigurationCheck.Validate(Config(values)).Should().ContainSingle().Which.Should().Contain("PublicWebUrl");
    }

    [Fact]
    public void OwnerPassword_MayBeAbsent_AfterFirstRelease()
    {
        // После первого релиза пароль владельца из окружения убирают — это не ошибка.
        var values = Valid();
        values.Remove("Bootstrap:Owner:Password");

        ProductionConfigurationCheck.Validate(Config(values)).Should().BeEmpty();
    }
}
