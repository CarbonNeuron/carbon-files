using CarbonFiles.Core.Configuration;
using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarbonFiles.Infrastructure.Services;

public sealed class DatabaseHealthService : BackgroundService
{
    private readonly SqliteConnection _connection;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(IOptions<CarbonFilesOptions> options, ILogger<DatabaseHealthService> logger)
    {
        _logger = logger;
        var connectionString = $"Data Source={options.Value.DbPath}";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app to fully start before first check
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunHealthCheck();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database health check");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }

    internal void RunHealthCheck()
    {
        DatabaseInitializer.RunIntegrityCheck(_connection, _logger);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
            cmd.Dispose();
            _logger.LogInformation("WAL checkpoint completed on shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to checkpoint WAL on shutdown");
        }
        finally
        {
            _connection.Close();
            _connection.Dispose();
        }
    }

    public override void Dispose()
    {
        _connection.Dispose();
        base.Dispose();
    }
}
