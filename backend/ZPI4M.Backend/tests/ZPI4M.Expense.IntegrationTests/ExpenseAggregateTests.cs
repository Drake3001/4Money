using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Expense.Services.Reads;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Expenses;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Settlements;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.Receipt.Services;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseAggregateTests : PostgresFixture
{
	private static readonly Guid Group = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid Other = Guid.Parse("10000000-0000-0000-0000-000000000002");

	private static CreateExpenseRequest Request(string amount = "10", bool unequal = false,
		Guid? payer = null, IReadOnlyList<CreateExpenseShareRequest>? shares = null) => new()
		{
			GroupId = Group,
			PayerId = payer ?? UserId,
			Name = "Aggregate integration",
			Amount = amount,
			TransactionTime = new DateOnly(2026, 11, 1),
			Items = [],
			Shares = shares ?? [new() { UserId = Other, Percentage = unequal ? "66.67" : "50" }, new() { UserId = UserId, Percentage = unequal ? "33.33" : "50" }]
		};

	private async Task<(long Notifications, long Recipients)> NotificationCountsAsync() =>
		(await ScalarAsync<long>("SELECT count(*) FROM notifications.notification"),
			await ScalarAsync<long>("SELECT count(*) FROM notifications.notification_recipient"));

	[PostgresFact]
	public async Task CreationNotifiesOtherCurrentMembersIncludingPayerAndNonparticipants()
	{
		await SeedAsync();
		var third = Guid.Parse("10000000-0000-0000-0000-000000000003");
		await ExecuteAsync($"""
			INSERT INTO grouping.group_user (group_id, user_id, joined_at, is_member) VALUES
			('{Group}', '{third}', CURRENT_TIMESTAMP, true),
			('{Group}', '10000000-0000-0000-0000-000000000004', CURRENT_TIMESTAMP, true),
			('{Group}', '10000000-0000-0000-0000-000000000010', CURRENT_TIMESTAMP, false);
			""");
		await using var session = new Session(ConnectionString);
		var result = await session.Expenses.CreateAsync(UserId,
			Request(payer: Other, shares: [new() { UserId = UserId, Percentage = "100" }]));
		Assert.True(result.IsSuccess);
		var expenseId = result.Value!.Expense.Id;
		await using var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(ConnectionString).Options);
		var notification = await inbox.Notifications.AsNoTracking().SingleAsync(value => value.FkExpenseId == expenseId);
		Assert.Equal(NotificationType.ExpenseAdded, notification.Type);
		Assert.Equal("Nowy wydatek", notification.Title);
		Assert.Equal("Dodano wydatek „Aggregate integration” za 10,00 zł w grupie „Dom”.", notification.Text);
		Assert.Null(notification.FkGroupId);
		var recipients = await inbox.NotificationRecipients.AsNoTracking()
			.Where(value => value.FkNotificationId == notification.Id).ToArrayAsync();
		Assert.Equal(new[] { Other, third }.Order(), recipients.Select(value => value.FkUserId).Order());
		Assert.All(recipients, recipient => Assert.Null(recipient.ReadAt));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CreationWithoutOtherActiveRecipientsCommitsWithoutNotification(bool formerMember)
	{
		await SeedAsync();
		if (formerMember)
			await ExecuteAsync($"UPDATE grouping.group_user SET is_member = false WHERE group_id = '{Group}' AND user_id = '{Other}'");
		else
			await ExecuteAsync($"UPDATE \"user\".\"user\" SET is_active = false WHERE _id = '{Other}'");
		var before = await NotificationCountsAsync();
		await using var session = new Session(ConnectionString);
		var result = await session.Expenses.CreateAsync(UserId,
			Request(shares: [new() { UserId = UserId, Percentage = "100" }]));
		Assert.True(result.IsSuccess);
		Assert.Equal(before, await NotificationCountsAsync());
		Assert.Equal(5L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NotificationFailureRollsBackExpenseBalancesAndInbox(bool afterSave)
	{
		await SeedAsync();
		var balances = await Snapshot();
		var notifications = await NotificationCountsAsync();
		await using var session = new Session(ConnectionString,
			notificationInterceptor: afterSave ? new FailAfterSave() : new FailBeforeNotificationSave());
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Expenses.CreateAsync(UserId, Request()));
		await AssertNoCreation(balances);
		Assert.Equal(notifications, await NotificationCountsAsync());
	}

	private Task<string> Snapshot() => ScalarAsync<string>("""
		SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id, group_id)::text FROM grouping.group_user m
		""");

	private async Task AssertNoCreation(string before)
	{
		Assert.Equal(before, await Snapshot());
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(7L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item"));
		Assert.Equal(8L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_share"));
	}

	[PostgresTheory]
	[InlineData("10", 10, 5, 5, false)]
	[InlineData("0.01", 0.01, 0.01, 0, false)]
	[InlineData("0.01", 0.01, 0, 0.01, true)]
	public async Task CreationUsesExactPreparedSharesAndLeavesSettlementTotalsUnchanged(string amount, double paid, double first, double second, bool unequal)
	{
		await SeedAsync();
		await using var session = new Session(ConnectionString);
		var before = await session.Grouping.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group).ToDictionaryAsync(m => m.FkUserId);
		var result = await session.Expenses.CreateAsync(UserId, Request(amount, unequal));
		Assert.True(result.IsSuccess);
		await using var verify = new Session(ConnectionString);
		var after = await verify.Grouping.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group).ToDictionaryAsync(m => m.FkUserId);
		Assert.Equal(before[UserId].TotalPaid + (decimal)paid, after[UserId].TotalPaid);
		Assert.Equal(before[Other].TotalPaid, after[Other].TotalPaid);
		Assert.Equal(before[UserId].TotalShare + (decimal)first, after[UserId].TotalShare);
		Assert.Equal(before[Other].TotalShare + (decimal)second, after[Other].TotalShare);
		foreach (var member in after.Values)
		{
			Assert.Equal(before[member.FkUserId].TotalSettlementsSent, member.TotalSettlementsSent);
			Assert.Equal(before[member.FkUserId].TotalSettlementsReceived, member.TotalSettlementsReceived);
			Assert.Equal(result.Value!.Expense.Shares.Single(s => s.UserId == member.FkUserId).Amount,
				member.TotalShare - before[member.FkUserId].TotalShare);
		}
		Assert.Null(session.Expense.Database.CurrentTransaction);
		Assert.Null(session.Grouping.Database.CurrentTransaction);
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailureSavingEitherContextRollsBackBothModules(bool failGrouping)
	{
		await SeedAsync(); var before = await Snapshot();
		await using var session = new Session(ConnectionString, failGrouping ? null : new FailAfterSave(), failGrouping ? new FailAfterSave() : null);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Expenses.CreateAsync(UserId, Request()));
		await AssertNoCreation(before);
		Assert.Null(session.Expense.Database.CurrentTransaction);
		Assert.Null(session.Grouping.Database.CurrentTransaction);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Expenses.CreateAsync(UserId, Request()));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task OuterRejectionOrCancellationRollsBackAlreadySavedCreation(bool cancel)
	{
		await SeedAsync(); var before = await Snapshot();
		var notifications = await NotificationCountsAsync();
		await using var session = new Session(ConnectionString);
		using var cancellation = new CancellationTokenSource();
		async Task Run() => await session.Transactions.ExecuteAsync(async token =>
		{
			Assert.True((await session.Expenses.CreateAsync(UserId, Request(), token)).IsSuccess);
			if (cancel) cancellation.Cancel();
			return cancel;
		}, success => success, cancellation.Token);
		if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Run); else await Run();
		await AssertNoCreation(before);
		Assert.Equal(notifications, await NotificationCountsAsync());
		Assert.Null(session.Expense.Database.CurrentTransaction);
	}

	[PostgresFact]
	public async Task OverflowIsConflictAndPreviewAndReadsDoNotApplyContributions()
	{
		await SeedAsync();
		var notifications = await NotificationCountsAsync();
		await ExecuteAsync($"UPDATE grouping.group_user SET total_paid = 9999999999999999.99 WHERE group_id = '{Group}' AND user_id = '{UserId}'");
		var before = await Snapshot();
		await using var session = new Session(ConnectionString);
		Assert.True((await session.Expenses.PreviewAsync(UserId, new() { GroupId = Group, PayerId = UserId, Name = "Preview", Amount = "10", TransactionTime = Request().TransactionTime, Items = [], Shares = Request().Shares })).IsSuccess);
		Assert.True((await session.Queries.GetByIdAsync(UserId, ExpenseId)).IsSuccess);
		Assert.Equal(before, await Snapshot());
		Assert.Equal(ExpenseErrorCode.Conflict, (await session.Expenses.CreateAsync(UserId, Request())).Error);
		await AssertNoCreation(before);
		Assert.Equal(notifications, await NotificationCountsAsync());
	}

	[PostgresFact]
	public async Task ConcurrentExpensesAndSettlementPreserveAllFourTotals()
	{
		await SeedAsync();
		await using var first = new Session(ConnectionString);
		await using var second = new Session(ConnectionString);
		await using var settlement = new Session(ConnectionString);
		var before = await first.Grouping.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group).ToDictionaryAsync(m => m.FkUserId);
		var one = first.Expenses.CreateAsync(UserId, Request());
		var two = second.Expenses.CreateAsync(UserId, Request());
		var transfer = settlement.Settlements.CreateAsync(UserId, Group, new()
		{
			FromUserId = Other,
			ToUserId = UserId,
			Amount = 1,
			Method = SettlementMethod.Cash,
			SettledAt = DateTimeOffset.UtcNow.AddMinutes(-1)
		});
		await Task.WhenAll(one, two, transfer);
		Assert.True(one.Result.IsSuccess); Assert.True(two.Result.IsSuccess); Assert.Equal(201, transfer.Result.Status);
		await using var verify = new Session(ConnectionString);
		var after = await verify.Grouping.GroupMembers.AsNoTracking().Where(m => m.FkGroupId == Group).ToDictionaryAsync(m => m.FkUserId);
		Assert.Equal(before[UserId].TotalPaid + 20, after[UserId].TotalPaid);
		Assert.Equal(before[UserId].TotalShare + 10, after[UserId].TotalShare);
		Assert.Equal(before[Other].TotalShare + 10, after[Other].TotalShare);
		Assert.Equal(before[Other].TotalPaid, after[Other].TotalPaid);
		Assert.Equal(before[Other].TotalSettlementsSent + 1, after[Other].TotalSettlementsSent);
		Assert.Equal(before[UserId].TotalSettlementsReceived + 1, after[UserId].TotalSettlementsReceived);
		Assert.Equal(before[UserId].TotalSettlementsSent, after[UserId].TotalSettlementsSent);
		Assert.Equal(before[Other].TotalSettlementsReceived, after[Other].TotalSettlementsReceived);
	}

	[PostgresTheory]
	[InlineData("actor", ExpenseErrorCode.NotFound)]
	[InlineData("participant", ExpenseErrorCode.InvalidExpense)]
	[InlineData("archive", ExpenseErrorCode.NotFound)]
	[InlineData("currency", ExpenseErrorCode.InvalidExpense)]
	public async Task ChangesAfterPreparationAreRecheckedUnderTheGroupLock(string change, ExpenseErrorCode error)
	{
		await SeedAsync();
		var gate = new GateNames();
		await using var session = new Session(ConnectionString, names: gate);
		var creation = session.Expenses.CreateAsync(change == "actor" ? Other : UserId, Request());
		await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
		try
		{
			if (change is "actor" or "participant")
			{
				await using var mutation = new Session(ConnectionString);
				Assert.Equal(204, (await mutation.Members.RemoveAsync(UserId, Group, Other)).Status);
			}
			else
			{
				await using var mutation = new Session(ConnectionString);
				await mutation.Transactions.ExecuteAsync(async token =>
				{
					var group = await mutation.Groups.LockAsync(Group, token);
					if (change == "archive") group!.IsArchived = true; else group!.CurrencyCode = "EUR";
					await mutation.Grouping.SaveChangesAsync(token); return true;
				}, success => success);
			}
		}
		finally { gate.Release.TrySetResult(); }
		var result = await creation;
		Assert.Equal(error, result.Error);
		if (change == "participant") Assert.Contains(result.ValidationErrors, e => e.MemberNames.Contains("Shares[0].UserId"));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MembershipOrArchiveChangeWaitsForAnExpenseHoldingTheGroupLock(bool archive)
	{
		await SeedAsync();
		var gate = new SaveGate();
		var observation = new ObserveLock();
		await using var creationScope = new Session(ConnectionString, groupingInterceptor: gate);
		await using var mutationScope = new Session(ConnectionString, groupingInterceptor: observation);
		var creation = creationScope.Expenses.CreateAsync(UserId, Request());
		await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
		Task mutation;
		if (archive)
			mutation = mutationScope.Transactions.ExecuteAsync(async token =>
			{
				var group = await mutationScope.Groups.LockAsync(Group, token);
				group!.IsArchived = true; await mutationScope.Grouping.SaveChangesAsync(token); return true;
			}, success => success);
		else mutation = mutationScope.Members.RemoveAsync(UserId, Group, Other);
		try
		{
			await observation.Executing.Task.WaitAsync(TimeSpan.FromSeconds(15));
			Assert.False(mutation.IsCompleted);
		}
		finally { gate.Release.TrySetResult(); }
		await Task.WhenAll(creation, mutation);
		Assert.True(creation.Result.IsSuccess);
		Assert.Equal(5L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(665m, await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{Group}' AND user_id = '{Other}'"));
		if (archive) Assert.True(await ScalarAsync<bool>($"SELECT is_archived FROM grouping.\"group\" WHERE id = '{Group}'"));
		else Assert.False(await ScalarAsync<bool>($"SELECT is_member FROM grouping.group_user WHERE group_id = '{Group}' AND user_id = '{Other}'"));
	}

	[PostgresFact]
	public async Task RejectedNestedCreationPoisonsOuterTransactionEvenIfFailureIsHandled()
	{
		await SeedAsync(); var before = await Snapshot();
		await using var session = new Session(ConnectionString);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Transactions.ExecuteAsync(async token =>
		{
			Assert.True((await session.Expenses.CreateAsync(UserId, Request(), token)).IsSuccess);
			Assert.False((await session.Expenses.CreateAsync(UserId, Request("-1"), token)).IsSuccess);
			return true;
		}, success => success));
		await AssertNoCreation(before);
	}

	[PostgresFact]
	public async Task CaughtPreparationFailureStillRollsBackOuterCreation()
	{
		await SeedAsync(); var before = await Snapshot();
		var names = new ToggleNames();
		await using var session = new Session(ConnectionString, names: names);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Transactions.ExecuteAsync(async token =>
		{
			Assert.True((await session.Expenses.CreateAsync(UserId, Request(), token)).IsSuccess);
			names.Fail = true;
			await Assert.ThrowsAsync<InvalidOperationException>(() => session.Expenses.CreateAsync(UserId, Request(), token));
			return true;
		}, success => success));
		await AssertNoCreation(before);
	}

	[PostgresFact]
	public async Task EnlistmentRejectsDifferentConnectionsAndUnmanagedTransactions()
	{
		await SeedAsync(); var before = await Snapshot();
		await using var session = new Session(ConnectionString);
		await using var unrelated = new ExpenseDbContext(new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).Options);
		await Assert.ThrowsAsync<InvalidOperationException>(() => session.Transactions.ExecuteAsync(async token =>
		{
			try { await session.Transactions.EnlistAsync(unrelated, token); } catch (InvalidOperationException) { }
			return true;
		}, success => success));
		await using var fresh = new Session(ConnectionString);
		await using var unmanaged = await fresh.Expense.Database.BeginTransactionAsync();
		await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.Expenses.CreateAsync(UserId, Request()));
		await AssertNoCreation(before);
	}

	[PostgresFact]
	public async Task SeedRebuildAllocatesTheSameRemainderCentsAsExpense()
	{
		await SeedAsync();
		await ExecuteAsync($"UPDATE expense.expense SET amount = 0.01 WHERE _id = '{ExpenseId}'");
		var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", "seed_local.sql"));
		var start = script.IndexOf("WITH exact_shares AS", StringComparison.Ordinal);
		var end = script.IndexOf(';', start) + 1;
		await ExecuteAsync(script[start..end]);
		Assert.Equal(900.01m, await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{Group}' AND user_id = '{UserId}'"));
		Assert.Equal(600m, await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{Group}' AND user_id = '{Other}'"));
	}

	private sealed class Session : IAsyncDisposable
	{
		private readonly NpgsqlConnection connection;
		public ExpenseDbContext Expense { get; }
		public GroupingDbContext Grouping { get; }
		private NotificationsDbContext Inbox { get; }
		private UserDbContext Users { get; }
		private ReceiptDbContext Receipts { get; }
		private BankingDbContext Banking { get; }
		public GroupingTransactionCoordinator Transactions { get; }
		public IGroupRepository Groups { get; }
		public IGroupMemberService Members { get; }
		public ExpenseService Expenses { get; }
		public ExpenseQueryService Queries { get; }
		public GroupSettlementService Settlements { get; }
		public Session(string connectionString, IInterceptor? expenseInterceptor = null, IInterceptor? groupingInterceptor = null, IUserDisplayNameLookup? names = null, IInterceptor? notificationInterceptor = null)
		{
			connection = new NpgsqlConnection(connectionString);
			var expenseOptions = new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(connection, contextOwnsConnection: false);
			if (expenseInterceptor is not null) expenseOptions.AddInterceptors(expenseInterceptor);
			Expense = new(expenseOptions.Options);
			var groupingOptions = new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(connection, contextOwnsConnection: false);
			if (groupingInterceptor is not null) groupingOptions.AddInterceptors(groupingInterceptor);
			Grouping = new(groupingOptions.Options);
			var inboxOptions = new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(connection, contextOwnsConnection: false);
			if (notificationInterceptor is not null) inboxOptions.AddInterceptors(notificationInterceptor);
			Inbox = new(inboxOptions.Options);
			Users = new(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(connectionString).Options);
			Banking = new(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options);
			Receipts = new(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(connectionString).Options);
			var receiptDependencies = new ReceiptTestDependencies();
			var receiptService = new ReceiptService(new ReceiptRepository(Receipts), Receipts, receiptDependencies, receiptDependencies,
				NullLogger<ReceiptService>.Instance, TimeProvider.System, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
			Transactions = new(Grouping, Inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
			Groups = new GroupRepository(Grouping);
			var members = new GroupMemberRepository(Grouping);
			Members = new GroupMemberService(Groups, members, Grouping, Transactions);
			var aggregates = new GroupMemberAggregateService(Groups, members, Transactions);
			var notifications = new NotificationService(new NotificationRepository(Inbox), new NotificationRecipientRepository(Inbox), Inbox, new UserLookupService(Users));
			var expenseRepository = new ExpenseRepository(Expense);
			var items = new ExpenseItemRepository(Expense);
			var shares = new ExpenseShareRepository(Expense);
			var categories = new CategoryRepository(Expense);
			var metadata = new GroupExpenseMetadataLookup(Groups);
			var calculations = new ExpenseCalculationService();
			Queries = new(expenseRepository, items, shares, categories, Members, metadata, names ?? new UserDisplayNameLookup(Users), calculations);
			Expenses = new(Expense, expenseRepository, items, shares, categories,
				Members, new ExpenseRequestService(calculations), Queries, new UnusedBanking(), metadata,
				Transactions, new GroupExpenseContributionService(Grouping, Groups, members, aggregates, Transactions), new GroupExpenseNotificationService(members, notifications, Transactions), receiptService, Receipts, new ReceiptOcrProof(Options.Create(new ReceiptOcrProofOptions { SigningKey = Convert.ToBase64String(new byte[32]) }), TimeProvider.System), Banking,
				new BankTransactionAssignmentService(new BankTransactionAssignmentRepository(Banking)));
			Settlements = new(Grouping, Groups, members, new GroupSettlementRepository(Grouping), aggregates, Transactions, TimeProvider.System, null!, null!);
		}
		public async ValueTask DisposeAsync()
		{
			await Expense.DisposeAsync(); await Grouping.DisposeAsync(); await Inbox.DisposeAsync(); await Users.DisposeAsync(); await Receipts.DisposeAsync(); await Banking.DisposeAsync(); await connection.DisposeAsync();
		}
	}

	private sealed class SaveGate : SaveChangesInterceptor
	{
		public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); return result;
		}
	}

	private sealed class ObserveLock : DbCommandInterceptor
	{
		public TaskCompletionSource Executing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
		{
			if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Executing.TrySetResult();
			return ValueTask.FromResult(result);
		}
	}

	private sealed class FailBeforeNotificationSave : SaveChangesInterceptor
	{
		public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
			InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Injected notification failure.");
	}

	private sealed class FailAfterSave : SaveChangesInterceptor
	{
		public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Injected save failure.");
	}

	private sealed class ToggleNames : IUserDisplayNameLookup
	{
		public bool Fail { get; set; }
		public Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
		{
			if (Fail) throw new InvalidOperationException("Injected label failure.");
			return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "Test"));
		}
	}

	private sealed class GateNames : IUserDisplayNameLookup
	{
		public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
		{
			Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
			return ids.ToDictionary(id => id, _ => "Test");
		}
	}

	private sealed class UnusedBanking : IBankingReadService
	{
		public Task<BankingResult<PagedResult<BankAccountDetails>>> GetAccountsAsync(Guid userId, BankAccountReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<BankAccountDetails>> GetAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<PagedResult<BankTransactionDetails>>> GetTransactionsAsync(Guid userId, BankTransactionReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<BankTransactionDetails>> GetTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
