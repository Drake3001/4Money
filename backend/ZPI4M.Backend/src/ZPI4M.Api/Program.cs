using Microsoft.EntityFrameworkCore;
using DotNetEnv;
using ZPI4M.Banking.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Limits.Database;
using ZPI4M.Receipt.Database;
using ZPI4M.User.Database;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file
Env.Load();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add database contexts
var dbConnectionString = Environment.GetEnvironmentVariable("DB_LOCAL_CONNECTION_STRING");
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Banking", "banking")));
builder.Services.AddDbContext<ExpenseDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Expense", "expense")));
builder.Services.AddDbContext<GroupingDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Grouping", "grouping")));
builder.Services.AddDbContext<LimitsDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Limits", "limits")));
builder.Services.AddDbContext<ReceiptDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Receipt", "receipt")));
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_User", "user")));

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
