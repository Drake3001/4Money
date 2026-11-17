using DotNetEnv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Npgsql;
using ZPI4M.Banking.ApiControllers;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankingConnectionRepository;
using ZPI4M.Banking.Repositories.BankRefreshRepository;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink;
using ZPI4M.Database;
using ZPI4M.Expense.ApiControllers;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Repositories.CategoryGroupLimitRepository;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.Receipt.Services;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Storage;
using ZPI4M.User.ApiControllers;
using ZPI4M.User.Database;
using ZPI4M.User.Integrations.Google;
using ZPI4M.User.Integrations.Smtp;
using ZPI4M.User.Repositories.UserRepository;
using ZPI4M.User.Repositories.UserTokenRepository;
using ZPI4M.User.Services.AccountDeactivation;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.BankingProfile;
using ZPI4M.User.Services.EmailChange;
using ZPI4M.User.Services.EmailConfirmation;
using ZPI4M.User.Services.Lookup;
using ZPI4M.User.Services.PasswordReset;
using ZPI4M.User.Services.Profile;
using ZPI4M.User.Services.RateLimiting;
using ZPI4M.User.Services.Registration;
using ZPI4M.User.Services.Tokens;

// Load environment variables from .env file
Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var frontendUrl = builder.Configuration["FRONTEND_URL"] ?? "";

// Add services to the container.

builder.Services.AddControllers()
	.AddApplicationPart(typeof(UsersController).Assembly)
	.AddApplicationPart(typeof(ExpensesController).Assembly)
	.AddApplicationPart(typeof(BankingController).Assembly)
	.AddApplicationPart(typeof(ZPI4M.Grouping.ApiControllers.GroupsController).Assembly);
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();
builder.Services.AddTink(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddOptions<BankingConnectionOptions>().BindConfiguration("Banking")
 .Validate(options => options.AttemptLifetimeMinutes is >= 1 and <= 60, "Attempt lifetime must be between 1 and 60 minutes.");
builder.Services.AddScoped<BankingConnectionRepository>();
builder.Services.AddScoped<IBankConnectionAttemptRepository>(provider => provider.GetRequiredService<BankingConnectionRepository>());
builder.Services.AddScoped<IBankConnectionRepository>(provider => provider.GetRequiredService<BankingConnectionRepository>());
builder.Services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
builder.Services.AddOptions<BankRefreshOptions>().BindConfiguration("Banking:Refresh")
 .ValidateDataAnnotations().Validate(value => value.HeartbeatSeconds * 2 < value.LeaseSeconds,
  "Refresh heartbeat must be shorter than half the lease.");
builder.Services.AddScoped<BankRefreshRepository>();
builder.Services.AddScoped<BankRefreshService>();
builder.Services.AddScoped<BankRefreshProcessor>();
builder.Services.AddHostedService<BankRefreshWorker>();
builder.Services.AddScoped<BankingConnectionService>();
builder.Services.AddScoped<IBankingConnectionService>(provider => provider.GetRequiredService<BankingConnectionService>());
builder.Services.AddScoped<IBankingCallbackService>(provider => provider.GetRequiredService<BankingConnectionService>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
{
	var name = context.Description.ActionDescriptor.AttributeRouteInfo?.Name;
	if (name is "createExpense" or "updateExpense" && operation.RequestBody?.Content is { } content)
	{
		content["multipart/form-data"] = new OpenApiMediaType
		{
			Schema = new OpenApiSchema
			{
				Type = JsonSchemaType.Object,
				Required = new HashSet<string> { "expense", "file" },
				Properties = new Dictionary<string, IOpenApiSchema>
				{
					["expense"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Expense form serialized as JSON." },
					["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
					["ocrProof"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ReceiptOcrProof.MaximumLength }
				}
			}
		};
	}
	return Task.CompletedTask;
}));
builder.Services.AddUserRateLimiting(builder.Configuration);

// Add database context
var dbConnectionString = Environment.GetEnvironmentVariable("DB_LOCAL_CONNECTION_STRING");
builder.Services.AddScoped<NpgsqlConnection>(_ => new NpgsqlConnection(dbConnectionString));
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseNpgsql(dbConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public")));

// Grouping module
builder.Services.AddScoped<ZPI4M.Grouping.Services.ExpenseMetadata.IGroupExpenseMetadataLookup, ZPI4M.Grouping.Services.ExpenseMetadata.GroupExpenseMetadataLookup>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Expenses.IGroupExpenseContributionService, ZPI4M.Grouping.Services.Expenses.GroupExpenseContributionService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Expenses.IGroupExpenseNotificationService, ZPI4M.Grouping.Services.Expenses.GroupExpenseNotificationService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Suggestions.IGroupSettlementSuggestionService, ZPI4M.Grouping.Services.Suggestions.GroupSettlementSuggestionService>();
builder.Services.AddSingleton<ZPI4M.Grouping.Services.Suggestions.SettlementSuggestionCalculator>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Aggregates.IGroupMemberAggregateService, ZPI4M.Grouping.Services.Aggregates.GroupMemberAggregateService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Settlements.IGroupSettlementService, ZPI4M.Grouping.Services.Settlements.GroupSettlementService>();
builder.Services.AddScoped<ZPI4M.Grouping.Repositories.GroupSettlementRepository.IGroupSettlementRepository, ZPI4M.Grouping.Repositories.GroupSettlementRepository.GroupSettlementRepository>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Members.IGroupMemberService, ZPI4M.Grouping.Services.Members.GroupMemberService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Transactions.IGroupingTransactionCoordinator, ZPI4M.Grouping.Services.Transactions.GroupingTransactionCoordinator>();
builder.Services.AddScoped<ZPI4M.Grouping.Repositories.GroupInvitationRepository.IGroupInvitationRepository, ZPI4M.Grouping.Repositories.GroupInvitationRepository.GroupInvitationRepository>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Invitations.IGroupInvitationService, ZPI4M.Grouping.Services.Invitations.GroupInvitationService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Groups.IGroupIconService, ZPI4M.Grouping.Services.Groups.GroupIconService>();
builder.Services.AddScoped<ZPI4M.Grouping.Services.Groups.IGroupService, ZPI4M.Grouping.Services.Groups.GroupService>();
builder.Services.AddDbContext<GroupingDbContext>((provider, options) =>
	options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();

// Expense module
builder.Services.AddExpenseServices();
builder.Services.AddDbContext<ExpenseDbContext>((provider, options) =>
	options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IExpenseItemRepository, ExpenseItemRepository>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IExpenseShareRepository, ExpenseShareRepository>();

// Banking module
// Factory contexts keep independent connections for Banking connection persistence.
builder.Services.AddScoped(provider => new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>()
	.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false).Options));
builder.Services.AddDbContextFactory<BankingDbContext>(options => options.UseNpgsql(dbConnectionString), ServiceLifetime.Scoped);
builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
builder.Services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();
builder.Services.AddScoped<IBankingReadService, BankingReadService>();
builder.Services.AddScoped<IBankTransactionAssignmentRepository, BankTransactionAssignmentRepository>();
builder.Services.AddScoped<IBankTransactionAssignmentService, BankTransactionAssignmentService>();

// Limits module
builder.Services.AddDbContext<LimitsDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<ICategoryGroupLimitRepository, CategoryGroupLimitRepository>();

// Receipt module
builder.Services.AddDbContext<ReceiptDbContext>((provider, options) =>
	options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddReceiptAnalysis(builder.Configuration);
builder.Services.AddObjectStorage(builder.Configuration);

// User module
builder.Services.AddUserAuthentication(builder.Configuration);
builder.Services.AddUserGoogleAuthentication(builder.Configuration);
builder.Services.AddScoped<IAccountDeactivationService, AccountDeactivationService>();
builder.Services.AddOptions<AccountDeactivationOptions>()
	.Bind(builder.Configuration.GetSection(AccountDeactivationOptions.SectionName))
	.ValidateDataAnnotations()
	.ValidateOnStart();
builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(dbConnectionString));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserLookupService, UserLookupService>();
builder.Services.AddScoped<IUserDisplayNameLookup, UserDisplayNameLookup>();
builder.Services.AddScoped<IUserTokenRepository, UserTokenRepository>();
builder.Services.AddScoped<IPasswordHasher<ZPI4M.User.Models.User>, PasswordHasher<ZPI4M.User.Models.User>>();
builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IUserIconService, UserIconService>();
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
builder.Services.AddOptions<UserEmailLinkOptions>()
	.BindConfiguration(UserEmailLinkOptions.SectionName)
	.Configure(options => options.FrontendBaseUrl = frontendUrl)
	.Validate(UserEmailLinkOptions.IsValid, "FRONTEND_URL must be an absolute HTTP(S) URL without credentials, query or fragment.")
	.ValidateOnStart();
builder.Services.AddSingleton<UserEmailMessages>();

// Notifications module
builder.Services.AddDbContext<NotificationsDbContext>((provider, options) =>
	options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationRecipientRepository, NotificationRecipientRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();
