using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Endpoints;
using CarbonFiles.Api.Hubs;
using CarbonFiles.Api.Serialization;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure;
using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateSlimBuilder(args);

// JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

// SignalR (JSON protocol only for AOT)
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, CarbonFilesJsonContext.Default);
    });

// OpenAPI
builder.Services.AddOpenApi();

// Infrastructure (EF Core, auth)
builder.Services.AddInfrastructure(builder.Configuration);

// Real-time notifications via SignalR
builder.Services.AddScoped<INotificationService, HubNotificationService>();

// CORS
var corsOrigins = builder.Configuration.GetValue<string>("CarbonFiles:CorsOrigins") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins == "*")
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy.AllowAnyMethod()
              .WithHeaders("Authorization", "Content-Type", "Content-Range", "X-Append")
              .WithExposedHeaders("Content-Range", "Accept-Ranges", "Content-Length", "ETag", "Last-Modified");
    });
});

var app = builder.Build();

// Ensure data directory exists
var dataDir = builder.Configuration.GetValue<string>("CarbonFiles:DataDir") ?? "./data";
var dbPath = builder.Configuration.GetValue<string>("CarbonFiles:DbPath") ?? "./data/carbonfiles.db";
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// Initialize database schema + WAL mode
// Both Migrate() and EnsureCreated() fail under Native AOT (design-time operations are trimmed).
// Use raw SQL DDL instead — idempotent via CREATE TABLE IF NOT EXISTS.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
    DatabaseInitializer.Initialize(db);
}

// Middleware
app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// Endpoints
app.MapHealthEndpoints();
app.MapKeyEndpoints();
app.MapBucketEndpoints();
app.MapUploadEndpoints();
app.MapUploadTokenEndpoints();
app.MapFileEndpoints();
app.MapTokenEndpoints();
app.MapShortUrlEndpoints();
app.MapStatsEndpoints();

// SignalR hub
app.MapHub<FileHub>("/hub/files");

// OpenAPI (always available)
app.MapOpenApi();

// Scalar UI (configurable)
if (builder.Configuration.GetValue<bool?>("CarbonFiles:EnableScalar") ?? true)
    app.MapScalarApiReference();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
