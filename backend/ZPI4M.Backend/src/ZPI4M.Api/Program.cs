using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Database;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file
Env.Load();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add database context
var dbConnectionString = Environment.GetEnvironmentVariable("DB_LOCAL_CONNECTION_STRING");
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
