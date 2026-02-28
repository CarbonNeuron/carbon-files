using CarbonFiles.Api.Auth;
using CarbonFiles.Api.Endpoints;
using CarbonFiles.Api.Serialization;
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

// Enable WAL mode + auto-migrate in development
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CarbonFilesDbContext>();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    if (app.Environment.IsDevelopment())
        db.Database.Migrate();
}

// Middleware
app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// Endpoints
app.MapHealthEndpoints();
app.MapKeyEndpoints();
app.MapBucketEndpoints();
app.MapUploadEndpoints();
app.MapFileEndpoints();

// SignalR hub — will be added in Task 21
// app.MapHub<FileHub>("/hub/files");

// OpenAPI + Scalar
app.MapOpenApi();
app.MapScalarApiReference();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
