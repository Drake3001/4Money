using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Expenses;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
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

public sealed class ExpenseCreationTests : PostgresFixture
{
	private static readonly Guid GroupId = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid CategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001");
	private static readonly Guid UnknownCategoryId = Guid.Parse("30000000-0000-0000-0000-000000000005");
	private static readonly Guid OtherUserId = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid ThirdUserId = Guid.Parse("10000000-0000-0000-0000-000000000003");
	private static readonly DateTime PurchaseTime = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

	[PostgresTheory]
	[InlineData(null, true, 10, 0)]
	[InlineData("15.00", true, 15, 5)]
	[InlineData("10.00", true, 10, 0)]
	[InlineData("15.00", false, 15, 15)]
	public async Task CreatesItemsAndAutomaticRemainderOnlyForExplicitTarget(string? amount, bool hasItems, int total, int remainder)
	{
		await SeedAsync();
		await using var db = OpenExpenseDatabase();
		var members = new MembershipStub();
		var result = await CreateService(db, members).CreateAsync(UserId, Request(amount, hasItems ? [Item()] : []));
		Assert.True(result.IsSuccess);
		var details = result.Value!.Expense;
		Assert.Equal(total, details.Amount);
		Assert.Equal("Purchase", details.Name);
		Assert.Null(details.Merchant);
		Assert.Equal(PurchaseTime, details.TransactionTime);
		Assert.Null(details.BankTransactionId);
		Assert.Null(details.ReceiptId);
		Assert.Equal(details.Amount, details.Items.Sum(item => item.Total));
		Assert.Equal(details.Amount, details.Shares.Sum(share => share.Amount));
		var automatic = details.Items.Where(item => item.IsSystemGenerated).ToArray();
		if (remainder == 0) Assert.Empty(automatic);
		else
		{
			var item = Assert.Single(automatic);
			Assert.Equal(UnknownCategoryId, item.CategoryId);
			Assert.Equal("Nieznane", item.Name);
			Assert.Equal(remainder, item.UnitPrice);
			Assert.Equal(1m, item.Quantity);
			Assert.Equal(0m, item.Discount);
		}
		await using var stored = OpenExpenseDatabase();
		Assert.Equal(details.Amount, (await stored.Expenses.SingleAsync(expense => expense.Id == details.Id)).Amount);
		Assert.Equal(details.Items.Count, await stored.ExpenseItems.CountAsync(item => item.FkExpenseId == details.Id));
		Assert.Equal(100m, await stored.ExpenseShares.Where(share => share.FkExpenseId == details.Id).SumAsync(share => share.Percentage));
		var batch = Assert.Single(members.Batches);
		Assert.Equal((UserId, GroupId), (batch.Actor, batch.Group));
		Assert.Equal(new[] { UserId, OtherUserId }, batch.Users);
		Assert.Empty(members.Calls);
	}

	[PostgresFact]
	public async Task PersistsFractionalQuantitiesDiscountsFreeItemsAndStandaloneNegativeItems()
	{
		await SeedAsync();
		await using var db = OpenExpenseDatabase();
		var result = await CreateService(db).CreateAsync(UserId, Request(null,
			[Item("10.00", "1.500", "-2.00"), Item("-1.00"), Item("0.00")]));
		Assert.True(result.IsSuccess);
		Assert.Equal(12m, result.Value!.Expense.Amount);
		Assert.Contains(result.Value.Expense.Items, item => item.Quantity == 1.5m && item.Discount == -2m && item.Total == 13m);
		Assert.Contains(result.Value.Expense.Items, item => item.UnitPrice == -1m && item.Discount == 0m);
		Assert.DoesNotContain(result.Value.Expense.Items, item => item.IsSystemGenerated);
	}

	[PostgresFact]
	public async Task ShareAmountsUseLargestRemaindersAndOrdinalUserIdForTies()
	{
		await SeedAsync();
		await ExecuteAsync($"INSERT INTO grouping.group_user (group_id, user_id, joined_at) VALUES ('{GroupId}', '{ThirdUserId}', CURRENT_TIMESTAMP)");
		await using var db = OpenExpenseDatabase();
		var result = await CreateService(db).CreateAsync(UserId, Request("0.01", [],
			[Share(OtherUserId, "50"), Share(UserId, "50")]));
		Assert.True(result.IsSuccess);
		Assert.Equal(0.01m, result.Value!.Expense.Shares.Single(share => share.UserId == UserId).Amount);
		Assert.Equal(0m, result.Value.Expense.Shares.Single(share => share.UserId == OtherUserId).Amount);
		var unequal = await CreateService(db).CreateAsync(UserId, Request("0.01", [],
			[Share(UserId, "33.33"), Share(OtherUserId, "33.33"), Share(ThirdUserId, "33.34")]));
		Assert.True(unequal.IsSuccess);
		Assert.Equal(0.01m, unequal.Value!.Expense.Shares.Single(share => share.UserId == ThirdUserId).Amount);
	}

	[PostgresFact]
	public async Task RepeatedInputCreatesSeparateExpenses()
	{
		await SeedAsync();
		await using var firstDb = OpenExpenseDatabase();
		await using var secondDb = OpenExpenseDatabase();
		var request = Request("10", [Item()]);
		var first = await CreateService(firstDb).CreateAsync(UserId, request);
		var second = await CreateService(secondDb).CreateAsync(UserId, request);
		Assert.True(first.IsSuccess);
		Assert.True(second.IsSuccess);
		Assert.NotEqual(first.Value!.Expense.Id, second.Value!.Expense.Id);
		Assert.Equal(6L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData("unavailable")]
	[InlineData("null")]
	[InlineData("wrong-user")]
	public async Task InvalidMembershipServiceResponsesFailClosed(string response)
	{
		await SeedAsync();
		var members = new MembershipStub { InvalidResponse = response };
		await using var db = OpenExpenseDatabase();
		var result = await CreateService(db, members).CreateAsync(UserId, Request("10", [Item()]));
		Assert.Equal(ExpenseErrorCode.TemporarilyUnavailable, result.Error);
		Assert.Equal(4L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData("actor")]
	[InlineData("payer")]
	[InlineData("participant")]
	[InlineData("category")]
	[InlineData("system-category")]
	[InlineData("amount")]
	public async Task InvalidMembershipCategoryOrInputDoesNotWriteAnything(string invalid)
	{
		await SeedAsync();
		var members = new MembershipStub();
		var payer = invalid == "payer" ? ThirdUserId : UserId;
		if (invalid == "actor") members.Former.Add(UserId);
		if (invalid == "payer") members.Former.Add(ThirdUserId);
		if (invalid == "participant") members.Former.Add(OtherUserId);
		var category = invalid == "category" ? Guid.NewGuid() : invalid == "system-category" ? UnknownCategoryId : CategoryId;
		var request = new CreateExpenseRequest
		{
			GroupId = GroupId,
			PayerId = payer,
			Name = "Purchase",
			Amount = invalid == "amount" ? "9" : "10",
			TransactionTime = DateOnly.FromDateTime(PurchaseTime),
			Items = [Item(category: category)],
			Shares = [Share(UserId, "50"), Share(OtherUserId, "50")]
		};
		await using var db = OpenExpenseDatabase();
		var result = await CreateService(db, members).CreateAsync(UserId, request);
		Assert.False(result.IsSuccess);
		Assert.Equal(4L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense"));
		Assert.Equal(7L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense_item"));
		Assert.Equal(8L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense_share"));
	}

	[PostgresFact]
	public async Task FailureAfterDatabaseSaveRollsBackExpenseItemsAndShares()
	{
		await SeedAsync();
		await using var db = OpenExpenseDatabase(new FailAfterSave());
		await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(db).CreateAsync(UserId, Request("15", [Item()])));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense"));
		Assert.Equal(7L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense_item"));
		Assert.Equal(8L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense_share"));
	}

	[PostgresFact]
	public async Task CreatesExpenseItemsAndSharesInOneSaveChangesCall()
	{
		await SeedAsync();
		var saves = new SaveCounter();
		await using var db = OpenExpenseDatabase(saves);
		var result = await CreateService(db).CreateAsync(UserId, Request("15", [Item()]));
		Assert.True(result.IsSuccess);
		Assert.Equal(1, saves.SaveCount);
		Assert.Equal((1, 2, 2), saves.Added);
		var details = result.Value!.Expense;
		Assert.Equal(15m, details.Amount);
		Assert.Equal(15m, details.Shares.Sum(share => share.Amount));
		Assert.All(details.Shares, share => Assert.Equal(7.5m, share.Amount));
		await using var stored = OpenExpenseDatabase();
		Assert.Equal(15m, (await stored.Expenses.SingleAsync(expense => expense.Id == details.Id)).Amount);
		var items = await stored.ExpenseItems.Where(item => item.FkExpenseId == details.Id).ToArrayAsync();
		Assert.Equal(2, items.Length);
		Assert.Equal(5m, Assert.Single(items, item => item.IsSystemGenerated).UnitPrice);
		var shares = await stored.ExpenseShares.Where(share => share.FkExpenseId == details.Id).ToArrayAsync();
		Assert.Equal(2, shares.Length);
		Assert.Equal(100m, shares.Sum(share => share.Percentage));
	}

	[PostgresFact]
	public async Task ReadsAllowActiveMembersAndFormerPayersOrParticipantsButHideUnrelatedExpenses()
	{
		await SeedAsync();
		var members = new MembershipStub();
		await using var db = OpenExpenseDatabase();
		CreateService(db, members, out var service);
		Assert.True((await service.GetByIdAsync(OtherUserId, ExpenseId)).IsSuccess);
		var active = await service.GetByIdAsync(ThirdUserId, ExpenseId);
		Assert.True(active.IsSuccess);
		Assert.Equal(active.Value!.Expense.Amount, active.Value.Expense.Items.Sum(item => item.Total));
		Assert.Equal(active.Value.Expense.Amount, active.Value.Expense.Shares.Sum(share => share.Amount));
		Assert.Contains(members.Calls, call => call.Actor == ThirdUserId && call.Group == GroupId && call.User == ThirdUserId);
		CreateService(db, new MembershipStub { InvalidResponse = "unavailable" }, out var unavailableQueries);
		var unavailable = await unavailableQueries.GetByIdAsync(ThirdUserId, ExpenseId);
		Assert.Equal(ExpenseErrorCode.TemporarilyUnavailable, unavailable.Error);
		members.Former.UnionWith([UserId, OtherUserId, ThirdUserId]);
		Assert.True((await service.GetByIdAsync(UserId, ExpenseId)).IsSuccess);
		Assert.True((await service.GetByIdAsync(OtherUserId, ExpenseId)).IsSuccess);
		Assert.Equal(ExpenseErrorCode.NotFound, (await service.GetByIdAsync(ThirdUserId, ExpenseId)).Error);
		Assert.Equal(ExpenseErrorCode.NotFound, (await service.GetByIdAsync(UserId, Guid.NewGuid())).Error);
	}

	[PostgresFact]
	public async Task CancellationBeforeCreationDoesNotWriteAnything()
	{
		await SeedAsync();
		await using var db = OpenExpenseDatabase();
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(db).CreateAsync(UserId, Request("10", [Item()]), cancelled.Token));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT COUNT(*) FROM expense.expense"));
	}

	[PostgresFact]
	public async Task CreationForwardsCancellationToTheMembershipService()
	{
		await SeedAsync();
		await using var db = OpenExpenseDatabase();
		using var cancellation = new CancellationTokenSource();
		var members = new MembershipStub();
		var result = await CreateService(db, members).CreateAsync(UserId, Request("10", [Item()]), cancellation.Token);
		Assert.True(result.IsSuccess);
		Assert.NotEmpty(members.Tokens);
		Assert.All(members.Tokens, token => Assert.Equal(cancellation.Token, token));
	}

	private ExpenseDbContext OpenExpenseDatabase(params IInterceptor[] interceptors) => new(
		new DbContextOptionsBuilder<ExpenseDbContext>().UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);

	private readonly List<DbContext> _groupingContexts = [];

	public override async Task DisposeAsync()
	{
		foreach (var context in _groupingContexts) await context.DisposeAsync();
		await base.DisposeAsync();
	}

	private ExpenseService CreateService(ExpenseDbContext db, MembershipStub? members = null) =>
		CreateService(db, members, out _);

	private ExpenseService CreateService(ExpenseDbContext db, MembershipStub? members, out ExpenseQueryService queries)
	{
		var grouping = new GroupingDbContext(new DbContextOptionsBuilder<GroupingDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options);
		var inbox = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options);
		var receiptDb = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(ConnectionString).Options);
		var bankingDb = new BankingDbContext(new DbContextOptionsBuilder<BankingDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options);
		_groupingContexts.Add(bankingDb);
		_groupingContexts.Add(grouping); _groupingContexts.Add(inbox); _groupingContexts.Add(receiptDb);
		var receiptDependencies = new ReceiptTestDependencies();
		var receipts = new ReceiptService(new ReceiptRepository(receiptDb), receiptDb, receiptDependencies, receiptDependencies,
			NullLogger<ReceiptService>.Instance, TimeProvider.System);
		var transactions = new GroupingTransactionCoordinator(grouping, inbox, NullLogger<GroupingTransactionCoordinator>.Instance);
		var groupRepository = new GroupRepository(grouping);
		var memberships = new GroupMemberRepository(grouping);
		var aggregates = new GroupMemberAggregateService(groupRepository, memberships, transactions);
		var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		_groupingContexts.Add(users);
		var notifications = new NotificationService(new NotificationRepository(inbox), new NotificationRecipientRepository(inbox), inbox, new UserLookupService(users));
		var expenseRepository = new ExpenseRepository(db);
		var items = new ExpenseItemRepository(db);
		var shares = new ExpenseShareRepository(db);
		var categories = new CategoryRepository(db);
		var memberService = members ?? new MembershipStub();
		var metadata = new GroupMetadataStub();
		var calculations = new ExpenseCalculationService();
		queries = new ExpenseQueryService(expenseRepository, items, shares, categories, memberService, metadata, new UserNamesStub(), calculations);
		return new(db, expenseRepository, items, shares, categories,
			memberService, new ExpenseRequestService(calculations), queries, new NoBanking(), metadata,
			transactions, new GroupExpenseContributionService(grouping, groupRepository, memberships, aggregates, transactions), new GroupExpenseNotificationService(memberships, notifications, transactions), receipts, receiptDb, new ReceiptOcrProof(Options.Create(new ReceiptOcrProofOptions { SigningKey = Convert.ToBase64String(new byte[32]) }), TimeProvider.System), bankingDb,
			new BankTransactionAssignmentService(new BankTransactionAssignmentRepository(bankingDb)));
	}

	private static CreateExpenseRequest Request(string? amount, IReadOnlyList<CreateExpenseItemRequest> items,
		IReadOnlyList<CreateExpenseShareRequest>? shares = null, string name = " Purchase ", string? merchant = " ") => new()
		{
			GroupId = GroupId,
			PayerId = UserId,
			Name = name,
			Merchant = merchant,
			Amount = amount,
			TransactionTime = DateOnly.FromDateTime(PurchaseTime),
			Items = items,
			Shares = shares ?? [Share(UserId, "50"), Share(OtherUserId, "50")]
		};

	private static CreateExpenseItemRequest Item(string price = "10.00", string quantity = "1.000", string discount = "0.00", Guid? category = null) => new()
	{
		CategoryId = category ?? CategoryId,
		Name = " Product ",
		UnitPrice = price,
		Quantity = quantity,
		Discount = discount,
		Unit = " szt "
	};

	private static CreateExpenseShareRequest Share(Guid user, string percentage) => new() { UserId = user, Percentage = percentage };

	private sealed class GroupMetadataStub : IGroupExpenseMetadataLookup
	{
		public Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid actor, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<GroupExpenseMetadataResult> GetAsync(Guid groupId, CancellationToken cancellationToken = default) =>
			Task.FromResult(new GroupExpenseMetadataResult(200, new(groupId, "Dom", "PLN", false)));
	}

	private sealed class UserNamesStub : IUserDisplayNameLookup
	{
		public Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.Distinct().ToDictionary(id => id, _ => "Test"));
	}

	private sealed class NoBanking : IBankingReadService
	{
		public Task<BankingResult<PagedResult<BankAccountDetails>>> GetAccountsAsync(Guid userId, BankAccountReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<BankAccountDetails>> GetAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<PagedResult<BankTransactionDetails>>> GetTransactionsAsync(Guid userId, BankTransactionReadQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<BankingResult<BankTransactionDetails>> GetTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class MembershipStub : IGroupMemberService
	{
		public HashSet<Guid> Former { get; } = [];
		public List<(Guid Actor, Guid Group, Guid User)> Calls { get; } = [];
		public List<(Guid Actor, Guid Group, IReadOnlyCollection<Guid> Users)> Batches { get; } = [];
		public List<CancellationToken> Tokens { get; } = [];
		public string? InvalidResponse { get; init; }

		public Task<GroupingResult<GroupMemberResponse>> GetAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default)
		{
			ct.ThrowIfCancellationRequested();
			Calls.Add((actor, group, user));
			Tokens.Add(ct);
			if (InvalidResponse == "unavailable") return Task.FromResult(new GroupingResult<GroupMemberResponse>(503));
			if (InvalidResponse == "null") return Task.FromResult(new GroupingResult<GroupMemberResponse>(200));
			if (InvalidResponse == "wrong-user") user = Guid.Empty;
			if (InvalidResponse == "wrong-group") group = Guid.Empty;
			if (InvalidResponse is "wrong-user" or "wrong-group")
				return Task.FromResult(new GroupingResult<GroupMemberResponse>(200, new(user, group, true, PurchaseTime, 0, 0, 0, 0)));
			if (Former.Contains(actor) || group != GroupId || user != UserId && user != OtherUserId && user != ThirdUserId)
				return Task.FromResult(new GroupingResult<GroupMemberResponse>(404));
			return Task.FromResult(new GroupingResult<GroupMemberResponse>(200,
				new(user, group, !Former.Contains(user), PurchaseTime, 0, 0, 0, 0)));
		}

		public Task<GroupingResult<IReadOnlySet<Guid>>> GetActiveMemberIdsAsync(Guid actor, Guid group,
			IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
		{
			ct.ThrowIfCancellationRequested();
			Batches.Add((actor, group, userIds));
			Tokens.Add(ct);
			if (InvalidResponse == "unavailable") return Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(503));
			if (InvalidResponse == "null") return Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(200));
			if (InvalidResponse == "wrong-user")
				return Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(200, new HashSet<Guid> { Guid.Empty }));
			if (Former.Contains(actor) || group != GroupId) return Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(404));
			var active = userIds.Where(user => !Former.Contains(user) && (user == UserId || user == OtherUserId || user == ThirdUserId)).ToHashSet();
			return Task.FromResult(new GroupingResult<IReadOnlySet<Guid>>(200, active));
		}

		public Task<GroupingResult<PagedResult<GroupMemberResponse>>> ListAsync(Guid actor, Guid group, GroupMemberListRequest query, CancellationToken ct = default) => throw new NotSupportedException();
		public Task<GroupingResult> LeaveAsync(Guid actor, Guid group, CancellationToken ct = default) => throw new NotSupportedException();
		public Task<GroupingResult> RemoveAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default) => throw new NotSupportedException();
	}

	private sealed class SaveCounter : SaveChangesInterceptor
	{
		public int SaveCount { get; private set; }
		public (int Expenses, int Items, int Shares) Added { get; private set; }

		public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			SaveCount++;
			var context = eventData.Context!;
			Added = (
				context.ChangeTracker.Entries<ZPI4M.Expense.Models.Expense>().Count(entry => entry.State == EntityState.Added),
				context.ChangeTracker.Entries<ZPI4M.Expense.Models.ExpenseItem>().Count(entry => entry.State == EntityState.Added),
				context.ChangeTracker.Entries<ZPI4M.Expense.Models.ExpenseShare>().Count(entry => entry.State == EntityState.Added));
			return ValueTask.FromResult(result);
		}
	}

	private sealed class FailAfterSave : SaveChangesInterceptor
	{
		public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
			=> throw new InvalidOperationException("Injected failure after a database save.");
	}
}
