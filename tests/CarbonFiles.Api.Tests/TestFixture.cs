using CarbonFiles.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarbonFiles.Api.Tests;

public class TestFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private SqliteConnection _connection = null!;
    private string _tempDir = null!;
    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Shared in-memory SQLite connection (stays open for lifetime of tests)
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<CarbonFilesDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                // Use in-memory SQLite with shared connection
                services.AddDbContext<CarbonFilesDbContext>(options =>
                    options.UseSqlite(_connection));
            });

            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CarbonFiles:AdminKey"] = "test-admin-key",
                    ["CarbonFiles:DataDir"] = _tempDir,
                    ["CarbonFiles:DbPath"] = Path.Combine(_tempDir, "test.db"),
                });
            });
        });

        Client = _factory.CreateClient();

        // Ensure database schema is created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CreateAdminClient()
        => CreateAuthenticatedClient("test-admin-key");
}
