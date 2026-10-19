using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories;
using ZPI4M.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories;
using ZPI4M.User.ApiControllers;
using ZPI4M.User.Database;
using ZPI4M.User.Repositories;
using ZPI4M.User.Services.Registration;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file
Env.Load();

// Add services to the container.

builder.Services.AddControllers()
	.AddApplicationPart(typeof(UsersController).Assembly);
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
builder.Services.AddScoped<IExpenseShareRepository, ExpenseShareRepository>();

// Banking module
builder.Services.AddDbContext<BankingDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
builder.Services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();

// User module
builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
builder.Services.AddScoped<IPasswordHasher<ZPI4M.User.Models.User>, PasswordHasher<ZPI4M.User.Models.User>>();
builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();

// Notifications module
builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

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
