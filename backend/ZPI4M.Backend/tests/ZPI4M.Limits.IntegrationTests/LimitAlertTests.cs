using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Access;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Expenses;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories.GroupLimitRepository;
using ZPI4M.Limits.Repositories.UserLimitRepository;
using ZPI4M.Limits.Services.GroupLimits;
using ZPI4M.Limits.Services.Usage;
using ZPI4M.Limits.Services.UserLimits;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Limits.IntegrationTests;

public sealed partial class LimitAlertTests : PostgresFixture
{
	private async Task PrepareAsync() => await ExecuteAsync("UPDATE grouping.\"group\" SET currency_code='PLN'");
	private Scope Open(decimal threshold = 80, string? now = null, bool unavailable = false) => new(ConnectionString, threshold, now, unavailable);
	private async Task<Notification[]> AlertsAsync()
	{
		await using var s = Open();
		return await s.Inbox.Notifications.AsNoTracking().Include(n => n.NotificationRecipients).Where(n => n.Type == NotificationType.LimitApproaching || n.Type == NotificationType.LimitExceeded).OrderBy(n => n.CreatedAt).ToArrayAsync();
	}

	private sealed class Scope : IAsyncDisposable
	{
		private readonly NpgsqlConnection connection;
		private readonly UserDbContext usersDb;
		public UserDbContext UsersDb => usersDb;
		public ExpenseDbContext Expense { get; }
		public GroupingDbContext Grouping { get; }
		public LimitsDbContext Limits { get; }
		public NotificationsDbContext Inbox { get; }
		public GroupingTransactionCoordinator Tx { get; }
		public ExpenseService Expenses { get; }
		public ZPI4M.Limits.Services.Alerts.LimitAlertService Alerts { get; }
		public UserLimitService Users { get; }
		public GroupLimitService Groups { get; }

		public Scope(string connectionString, decimal threshold, string? now, bool unavailable)
		{
			connection = new(connectionString);
			Expense = new(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options);
			Grouping = new(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options);
			Limits = new(new DbContextOptionsBuilder<LimitsDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options);
			Inbox = new(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options);
			usersDb = new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(connectionString).Options);
			Tx = new(Grouping, Inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
			var groups = new GroupRepository(Grouping);
			var members = new GroupMemberRepository(Grouping);
			var memberService = new GroupMemberService(groups, members, Grouping, Tx, new UserDisplayNameLookup(usersDb));
			var categories = new CategoryRepository(Expense);
			var expenses = new ExpenseRepository(Expense);
			var items = new ExpenseItemRepository(Expense);
			var shares = new ExpenseShareRepository(Expense);
			var metadata = new GroupExpenseMetadataLookup(groups);
			var calculation = new ExpenseCalculationService();
			var notifications = new NotificationService(new NotificationRepository(Inbox), new NotificationRecipientRepository(Inbox), Inbox, new UserLookupService(usersDb));
			var clock = new Clock(now);
			var alerts = AlertFactory.Create(Limits, Expense, Grouping, Inbox, Tx, clock, threshold, notifications, unavailable ? new UnavailableReader() : null);
			Alerts = alerts;
			var usage = new LimitUsageService(new ExpenseAggregationService(expenses, metadata), new ExpenseSystemAggregationService(expenses, Tx));
			Users = new(Limits, new UserLimitRepository(Limits), new CategoryLookupService(categories), new ZPI4M.Limits.Services.LimitWriteExecutor(Limits, Tx), new(), new(clock), clock, usage, alerts);
			Groups = new(Limits, new GroupLimitRepository(Limits), new GroupAccessService(groups, members, Tx), new CategoryLookupService(categories), new ZPI4M.Limits.Services.LimitWriteExecutor(Limits, Tx), new(), new(clock), clock, usage, alerts);
			var queries = new ExpenseQueryService(expenses, items, shares, categories, memberService, metadata, new UserDisplayNameLookup(usersDb), calculation);
			Expenses = new(Expense, expenses, items, shares, categories, memberService, new ExpenseRequestService(calculation), queries, null!, metadata, Tx, new GroupExpenseContributionService(Grouping, groups, members, new GroupMemberAggregateService(groups, members, Tx), Tx), new GroupExpenseNotificationService(members, notifications, Tx, new UserDisplayNameLookup(usersDb)), null!, null!, null!, null!, null!, [alerts]);
		}

		public async Task<Guid> PersonalAsync(Guid owner, Guid? category = null, decimal amount = 100)
		{
			var result = await Users.CreateAsync(owner, new() { Amount = amount.ToString(CultureInfo.InvariantCulture), CurrencyCode = "PLN", Period = LimitPeriod.Month, CategoryId = category });
			Assert.Equal(201, result.Status);
			return result.Value!.Id;
		}

		private static CreateExpenseItemRequest[] Items(decimal amount, decimal? categoryAmount) => [new()
		{
			CategoryId = Category,
			Name = "Synthetic",
			UnitPrice = (categoryAmount ?? amount).ToString(CultureInfo.InvariantCulture),
			Quantity = "1"
		}, new()
		{
			CategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
			Name = "Other",
			UnitPrice = (amount - (categoryAmount ?? amount)).ToString(CultureInfo.InvariantCulture),
			Quantity = "1"
		}

		];
		public async Task<ExpenseDetails> CreateAsync(decimal amount, Guid? group = null, decimal? categoryAmount = null, Guid? owner = null, DateOnly? day = null)
		{
			var result = await Expenses.CreateAsync(Admin, new() { GroupId = group ?? GroupId, PayerId = Admin, Name = "Synthetic alert expense", TransactionTime = day ?? new(2026, 11, 14), Items = Items(amount, categoryAmount), Shares = [new() { UserId = owner ?? Admin, Percentage = "100" }] });
			Assert.True(result.IsSuccess, result.Error?.ToString() + string.Join(";", result.ValidationErrors.Select(e => e.ErrorMessage)));
			return result.Value!.Expense;
		}

		public async Task<ExpenseDetails> UpdateAsync(ExpenseDetails previous, decimal amount, Guid? owner = null, DateOnly? day = null)
		{
			var result = await Expenses.UpdateAsync(Admin, previous.Id, new() { Version = previous.Version.ToString("D"), PayerId = Admin, Name = "Updated alert expense", TransactionTime = day ?? new(2026, 11, 14), Items = Items(amount, null), Shares = [new() { UserId = owner ?? Admin, Percentage = "100" }] });
			Assert.True(result.IsSuccess, result.Error?.ToString());
			return result.Value!.Expense;
		}

		public async ValueTask DisposeAsync()
		{
			await Limits.DisposeAsync();
			await Expense.DisposeAsync();
			await Grouping.DisposeAsync();
			await Inbox.DisposeAsync();
			await usersDb.DisposeAsync();
			await connection.DisposeAsync();
		}
	}

	private sealed class UnavailableReader : IExpenseAggregationService, IExpenseSystemAggregationService
	{
		public Task<ExpenseResult<ExpenseUsageTotal>> GetGroupUsageTotalAsync(Guid groupId, DateOnly dateFrom, DateOnly dateTo, Guid? categoryId, CancellationToken cancellationToken = default) => Task.FromResult(ExpenseResult<ExpenseUsageTotal>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<ExpenseUsageTotal>> GetUsageTotalAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, Guid? categoryId, CancellationToken cancellationToken = default) => Task.FromResult(ExpenseResult<ExpenseUsageTotal>.Failure(ExpenseErrorCode.TemporarilyUnavailable));
		public Task<ExpenseResult<IReadOnlyList<ExpenseCategoryBreakdownItem>>> GetCategoryBreakdownAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpensePeriodCategoryTotal>>> GetExpenseTotalsByPeriodAndCategoryAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, ExpenseTotalsGranularity granularity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpenseDayOfWeekTotal>>> GetExpenseTotalsByDayOfWeekAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<ExpenseResult<IReadOnlyList<ExpenseDailyTotalsItem>>> GetDailyExpenseTotalsAsync(Guid actor, DateOnly dateFrom, DateOnly dateTo, ExpenseAggregationScope scope, Guid? groupId, IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	}

	private sealed class Clock(string? now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now is null ? new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero) : DateTimeOffset.Parse(now, CultureInfo.InvariantCulture);
	}
}
