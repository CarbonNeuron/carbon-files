using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Api.Tests.Endpoints;

public class DashboardTokenEndpointTests : IntegrationTestBase
{

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_AsAdmin_Returns201WithTokenAndExpiresAt()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/tokens/dashboard", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("expires_at").GetString().Should().NotBeNullOrEmpty();

        // Default expiry should be ~1 hour from now
        var expiresAt = doc.RootElement.GetProperty("expires_at").GetDateTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CreateToken_WithCustomExpiry_Returns201()
    {
        using var client = Fixture.CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api/tokens/dashboard", new { expires_in = "6h" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);

        var expiresAt = doc.RootElement.GetProperty("expires_at").GetDateTime();
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(6), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CreateToken_NoAuth_Returns403()
    {
        var response = await Fixture.Client.PostAsJsonAsync("/api/tokens/dashboard", new { }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("Admin access required");
    }

    [Fact]
    public async Task CreateToken_ExceedsCap_Returns400()
    {
        using var client = Fixture.CreateAdminClient();
        // "3d" is a valid ExpiryParser preset (3 days) that exceeds the 24h cap
        var response = await client.PostAsJsonAsync("/api/tokens/dashboard", new { expires_in = "3d" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("error");
    }

    [Fact]
    public async Task CreateToken_InvalidFormat_Returns400()
    {
        using var client = Fixture.CreateAdminClient();
        // "2d" is not a valid ExpiryParser preset
        var response = await client.PostAsJsonAsync("/api/tokens/dashboard", new { expires_in = "2d" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("error");
    }

    // ── Created token can access admin endpoints ────────────────────────

    [Fact]
    public async Task CreatedToken_CanAccessAdminEndpoints()
    {
        using var adminClient = Fixture.CreateAdminClient();
        var createResponse = await adminClient.PostAsJsonAsync("/api/tokens/dashboard", new { }, TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("token").GetString()!;

        // Use the dashboard token to access an admin-only endpoint (GET /api/keys)
        using var dashboardClient = Fixture.CreateAuthenticatedClient(token);
        var keysResponse = await dashboardClient.GetAsync("/api/keys", TestContext.Current.CancellationToken);
        keysResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Validate /me ────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateMe_WithValidToken_Returns200()
    {
        using var adminClient = Fixture.CreateAdminClient();
        var createResponse = await adminClient.PostAsJsonAsync("/api/tokens/dashboard", new { }, TestContext.Current.CancellationToken);
        var json = await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("token").GetString()!;

        using var dashboardClient = Fixture.CreateAuthenticatedClient(token);
        var meResponse = await dashboardClient.GetAsync("/api/tokens/dashboard/me", TestContext.Current.CancellationToken);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var meJson = await meResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var meDoc = JsonDocument.Parse(meJson);
        meDoc.RootElement.GetProperty("scope").GetString().Should().Be("admin");
        meDoc.RootElement.GetProperty("expires_at").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateMe_NoToken_Returns401()
    {
        var response = await Fixture.Client.GetAsync("/api/tokens/dashboard/me", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("No token provided");
    }

    [Fact]
    public async Task ValidateMe_InvalidToken_Returns401()
    {
        using var client = Fixture.CreateAuthenticatedClient("totally-invalid-jwt-token");
        var response = await client.GetAsync("/api/tokens/dashboard/me", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        json.Should().Contain("Invalid or expired token");
    }
}
