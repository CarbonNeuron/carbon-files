using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CarbonFiles.Api.Tests;

public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseInitializerTests()
    {
        // Use a temp file DB — in-memory SQLite doesn't support WAL
        var dbPath = Path.Combine(Path.GetTempPath(), $"cf_pragma_test_{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        DatabaseInitializer.Initialize(_connection);
    }

    [Fact]
    public void Initialize_SetsWalJournalMode()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("wal", result);
    }

    [Fact]
    public void Initialize_SetsSynchronousNormal()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        // synchronous=NORMAL is 1
        Assert.Equal(1, result);
    }

    [Fact]
    public void Initialize_SetsWalAutocheckpoint()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA wal_autocheckpoint;";
        var result = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(1000, result);
    }

    [Fact]
    public void Initialize_IntegrityCheckPasses()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("ok", result);
    }

    public void Dispose()
    {
        var dbPath = _connection.DataSource;
        _connection.Dispose();
        // Clean up temp DB files
        try { File.Delete(dbPath); } catch { }
        try { File.Delete(dbPath + "-wal"); } catch { }
        try { File.Delete(dbPath + "-shm"); } catch { }
    }
}
