using System.Net;
using System.Text.Json;

namespace Hms.ApiTests;

public class HttpContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

    [Fact]
    public async Task HealthLive_ReturnsHealthy_WithoutDatabase()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task HealthReady_DatabaseUnavailable_Returns503()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ReadJsonAsync(response)).GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("code").GetString().Should().Be("not_found");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnhandledException_Returns500_WithoutLeakingDetails()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(ApiFactory.ThrowPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        raw.Should().NotContain("секретная деталь").And.NotContain("InvalidOperationException").And.NotContain("   at ");
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("code").GetString().Should().Be("internal_error");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CorrelationId_ValidIncoming_IsEchoed()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationHeader, "web-req_123.abc:1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationHeader).Single().Should().Be("web-req_123.abc:1");
    }

    [Fact]
    public async Task CorrelationId_Missing_IsGenerated()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.GetValues(CorrelationHeader).Single().Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData(ApiFactory.ThrowPath)]
    public async Task CorrelationId_InHeader_MatchesProblemBody(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        var header = response.Headers.GetValues(CorrelationHeader).Single();
        (await ReadJsonAsync(response)).GetProperty("correlationId").GetString().Should().Be(header);
    }

    [Fact]
    public async Task CorrelationId_ValidIncoming_IsUsedInProblemBody()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Add(CorrelationHeader, "client-id-42");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        (await ReadJsonAsync(response)).GetProperty("correlationId").GetString().Should().Be("client-id-42");
    }

    [Theory]
    [InlineData("bad value with spaces")]
    [InlineData("inject\u0001log")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task CorrelationId_InvalidIncoming_IsReplaced(string incoming)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, incoming);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var echoed = response.Headers.GetValues(CorrelationHeader).Single();
        echoed.Should().NotBe(incoming).And.MatchRegex("^[0-9a-f]{32}$");
    }
}
