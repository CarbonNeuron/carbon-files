using CarbonFiles.Infrastructure.Data;
using Microsoft.Data.Sqlite;

var dbPath = Environment.GetEnvironmentVariable("CarbonFiles__DbPath")
    ?? args.FirstOrDefault()
    ?? "./data/carbonfiles.db";

var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dir != null) Directory.CreateDirectory(dir);

Console.WriteLine($"Initializing database: {dbPath}");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
DatabaseInitializer.Initialize(conn);

Console.WriteLine("Database initialization complete.");
