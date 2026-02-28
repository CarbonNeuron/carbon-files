using System.Net;
using System.Text.Json;
using CarbonFiles.Core.Models.Responses;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class HealthEndpointTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private HttpClient Client => Fixture.Client;

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var health = JsonSerializer.Deserialize<HealthResponse>(json, SnakeCaseOptions);

        health.Should().NotBeNull();
        health!.Status.Should().Be("healthy");
        health.Db.Should().Be("ok");
        health.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_ReturnsSnakeCaseJson()
    {
        var response = await Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"uptime_seconds\"");
        json.Should().Contain("\"db\"");
        // Verify snake_case, NOT PascalCase
        json.Should().NotContain("\"Status\"");
        json.Should().NotContain("\"UptimeSeconds\"");
    }
}
