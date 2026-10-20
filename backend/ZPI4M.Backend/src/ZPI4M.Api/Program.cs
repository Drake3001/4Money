using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories;
using ZPI4M.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Repositories.CategoryGroupLimitRepository;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.User.Database;
using ZPI4M.User.Repositories;

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

// Grouping module
builder.Services.AddDbContext<GroupingDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();

// Expense module
builder.Services.AddDbContext<ExpenseDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IExpenseItemRepository, ExpenseItemRepository>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IExpenseShareRepository, ExpenseShareRepository>();

// Banking module
builder.Services.AddDbContext<BankingDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
builder.Services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();

// Limits module
builder.Services.AddDbContext<LimitsDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<ICategoryGroupLimitRepository, CategoryGroupLimitRepository>();

// Receipt module
builder.Services.AddDbContext<ReceiptDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();

// User module
builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IUserRepository, UserRepository>();

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
