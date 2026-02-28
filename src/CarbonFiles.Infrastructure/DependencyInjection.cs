using CarbonFiles.Core.Configuration;
using CarbonFiles.Core.Interfaces;
using CarbonFiles.Infrastructure.Auth;
using CarbonFiles.Infrastructure.Data;
using CarbonFiles.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarbonFiles.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CarbonFilesOptions>(configuration.GetSection(CarbonFilesOptions.SectionName));

        var options = new CarbonFilesOptions();
        configuration.GetSection(CarbonFilesOptions.SectionName).Bind(options);

        // EF Core + SQLite
        services.AddDbContext<CarbonFilesDbContext>(opts =>
            opts.UseSqlite($"Data Source={options.DbPath}"));

        // Auth
        services.AddMemoryCache();
        services.AddSingleton(new JwtHelper(options.EffectiveJwtSecret));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IBucketService, BucketService>();

        // File services
        services.AddSingleton<FileStorageService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IUploadService, UploadService>();

        return services;
    }
}
