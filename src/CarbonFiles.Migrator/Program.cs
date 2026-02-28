using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var dbPath = Environment.GetEnvironmentVariable("CarbonFiles__DbPath")
    ?? args.FirstOrDefault()
    ?? "./data/carbonfiles.db";

var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (dir != null) Directory.CreateDirectory(dir);

Console.WriteLine($"Migrating database: {dbPath}");

var options = new DbContextOptionsBuilder<CarbonFilesDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new CarbonFilesDbContext(options);
db.Database.Migrate();
db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

Console.WriteLine("Migration complete.");
