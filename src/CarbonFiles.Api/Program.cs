using CarbonFiles.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddDbContext<CarbonFilesDbContext>(options =>
    options.UseSqlite($"Data Source=./data/carbonfiles.db"));

var app = builder.Build();

app.Run();
