using System.Reflection;
using Formula1Racing.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("db");

// Locally, resolve the database path against the project folder instead of the working directory.
if (builder.Environment.IsDevelopment())
{
    var projectDirectory = typeof(Program).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "ProjectDirectory").Value!;
    var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
    sqliteBuilder.DataSource = Path.Combine(projectDirectory, sqliteBuilder.DataSource);
    connectionString = sqliteBuilder.ToString();
}

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlite(connectionString)
    .UseSnakeCaseNamingConvention());

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
