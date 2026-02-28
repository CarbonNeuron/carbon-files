using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace CarbonFiles.Client;

/// <summary>
/// Extension methods for registering the CarbonFiles API client.
/// </summary>
public static class CarbonFilesClientExtensions
{
    /// <summary>
    /// Adds the CarbonFiles API client to the service collection.
    /// </summary>
    public static IHttpClientBuilder AddCarbonFilesClient(
        this IServiceCollection services,
        Uri baseAddress)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonOptions)
        };

        return services
            .AddRefitClient<ICarbonFilesApi>(refitSettings)
            .ConfigureHttpClient(c => c.BaseAddress = baseAddress);
    }
}
