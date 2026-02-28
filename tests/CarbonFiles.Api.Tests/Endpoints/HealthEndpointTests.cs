using System.Net;
using System.Text.Json;
using CarbonFiles.Core.Models.Responses;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class HealthEndpointTests : IClassFixture<TestFixture>
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _client;

    public HealthEndpointTests(TestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/healthz");
        var json = await response.Content.ReadAsStringAsync();
        var health = JsonSerializer.Deserialize<HealthResponse>(json, SnakeCaseOptions);

        health.Should().NotBeNull();
        health!.Status.Should().Be("healthy");
        health.Db.Should().Be("ok");
        health.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetHealth_ReturnsSnakeCaseJson()
    {
        var response = await _client.GetAsync("/healthz");
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"uptime_seconds\"");
        json.Should().Contain("\"db\"");
        // Verify snake_case, NOT PascalCase
        json.Should().NotContain("\"Status\"");
        json.Should().NotContain("\"UptimeSeconds\"");
    }
}
