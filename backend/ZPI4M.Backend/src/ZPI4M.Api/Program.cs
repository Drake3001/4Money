using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Repositories.CategoryGroupLimitRepository;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.User.ApiControllers;
using ZPI4M.User.Database;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.AccountDeactivation;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.EmailChange;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.PasswordReset;
using ZPI4M.User.Services.Profile;
using ZPI4M.User.Services.RateLimiting;
using ZPI4M.User.Services.Registration;
using ZPI4M.User.Services.Tokens;

// Load environment variables from .env file
Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
	.AddApplicationPart(typeof(UsersController).Assembly);
builder.Services.AddProblemDetails();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddUserRateLimiting(builder.Configuration);

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
builder.Services.AddUserAuthentication(builder.Configuration);
builder.Services.AddScoped<IAccountDeactivationService, AccountDeactivationService>();
builder.Services.AddOptions<AccountDeactivationOptions>()
	.Bind(builder.Configuration.GetSection(AccountDeactivationOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();
builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
builder.Services.AddScoped<IPasswordHasher<ZPI4M.User.Models.User>, PasswordHasher<ZPI4M.User.Models.User>>();
builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddSingleton<IUserTokenGenerator, UserTokenGenerator>();
builder.Services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
builder.Services.AddOptions<EmailConfirmationOptions>();
builder.Services.AddScoped<IEmailChangeService, EmailChangeService>();
builder.Services.AddOptions<EmailChangeOptions>()
	.Bind(builder.Configuration.GetSection(EmailChangeOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddOptions<PasswordResetOptions>()
	.Bind(builder.Configuration.GetSection(PasswordResetOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();
builder.Services.AddOptions<SmtpOptions>()
	.Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
	.ValidateDataAnnotations()
	.Validate(options =>
		(string.IsNullOrWhiteSpace(options.Username) && string.IsNullOrEmpty(options.Password)) ||
		(!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrEmpty(options.Password)),
		"SMTP username and password must both be provided or both be empty.");
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// Notifications module
builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
