using Xunit;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.UnitTests;

public sealed class BankTransactionAssignmentServiceTests
{
	private static readonly Guid TransactionId = Guid.NewGuid();
	private static readonly Guid ConsumerId = Guid.NewGuid();

	[Theory]
	[InlineData(BankTransactionConsumerType.Expense)]
	[InlineData(BankTransactionConsumerType.GroupSettlement)]
	public async Task AssignReturnsSnapshotAndRepeatedAssignmentSucceeds(BankTransactionConsumerType type)
	{
		var repository = new Repository();
		var service = new BankTransactionAssignmentService(repository);
		var expected = new BankTransactionAssignmentDetails(TransactionId, type, ConsumerId);
		Assert.Equal(expected, (await service.AssignAsync(TransactionId, type, ConsumerId)).Value);
		Assert.Equal(expected, (await service.AssignAsync(TransactionId, type, ConsumerId)).Value);
		Assert.Equal(expected, await service.GetAsync(TransactionId));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NeitherAssignNorReleaseCanChangeAnotherConsumer(bool differentType)
	{
		var repository = new Repository { Assignment = new() { BankTransactionId = TransactionId, ConsumerType = BankTransactionConsumerType.Expense, ConsumerId = ConsumerId } };
		var original = repository.Assignment;
		var service = new BankTransactionAssignmentService(repository);
		var type = differentType ? BankTransactionConsumerType.GroupSettlement : BankTransactionConsumerType.Expense;
		var id = differentType ? ConsumerId : Guid.NewGuid();
		Assert.Equal(BankingErrorCode.Conflict, (await service.AssignAsync(TransactionId, type, id)).Error);
		Assert.Equal(BankingErrorCode.Conflict, (await service.ReleaseAsync(TransactionId, type, id)).Error);
		Assert.Same(original, repository.Assignment);
		Assert.DoesNotContain("delete", repository.Calls);
	}

	[Fact]
	public async Task ReleaseIsConditionalAndIdempotentAndGetCanReturnNull()
	{
		var repository = new Repository { Assignment = new() { BankTransactionId = TransactionId, ConsumerType = BankTransactionConsumerType.Expense, ConsumerId = ConsumerId } };
		var service = new BankTransactionAssignmentService(repository);
		Assert.True((await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Value);
		Assert.False((await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Value);
		Assert.Null(await service.GetAsync(TransactionId));
		Assert.Single(repository.Calls, call => call == "delete");
	}

	[Fact]
	public async Task MissingTransactionStopsWritesBeforeReadingOrChangingAssignment()
	{
		var repository = new Repository { Exists = false };
		var service = new BankTransactionAssignmentService(repository);
		Assert.Equal(BankingErrorCode.NotFound, (await service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Error);
		Assert.Equal(BankingErrorCode.NotFound, (await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId)).Error);
		Assert.Equal(new[] { "lock", "lock" }, repository.Calls);
		Assert.Null(await service.GetAsync(TransactionId));
	}

	[Theory]
	[InlineData("transaction")]
	[InlineData("consumer")]
	[InlineData("type")]
	public async Task InvalidArgumentsAreRejectedBeforeRepositoryCalls(string invalid)
	{
		var repository = new Repository();
		var service = new BankTransactionAssignmentService(repository);
		var transaction = invalid == "transaction" ? Guid.Empty : TransactionId;
		var consumer = invalid == "consumer" ? Guid.Empty : ConsumerId;
		var type = invalid == "type" ? (BankTransactionConsumerType)99 : BankTransactionConsumerType.Expense;
		await Assert.ThrowsAnyAsync<ArgumentException>(() => service.AssignAsync(transaction, type, consumer));
		await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ReleaseAsync(transaction, type, consumer));
		if (invalid == "transaction") await Assert.ThrowsAnyAsync<ArgumentException>(() => service.GetAsync(transaction));
		Assert.Empty(repository.Calls);
	}

	[Theory]
	[InlineData("get")]
	[InlineData("lock")]
	[InlineData("insert")]
	[InlineData("delete")]
	public async Task RepositoryErrorsAndCancellationPropagate(string operation)
	{
		var repository = new Repository { FailureAt = operation };
		if (operation == "delete") repository.Assignment = new() { BankTransactionId = TransactionId, ConsumerType = BankTransactionConsumerType.Expense, ConsumerId = ConsumerId };
		var service = new BankTransactionAssignmentService(repository);
		foreach (var error in new Exception[] { new InvalidOperationException("synthetic failure"), new OperationCanceledException() })
		{
			repository.Failure = error;
			var actual = await Record.ExceptionAsync(async () =>
			{
				if (operation == "get") await service.GetAsync(TransactionId);
				else if (operation == "delete") await service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId);
				else await service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId);
			});
			Assert.Same(error, actual);
		}
	}

	[Fact]
	public async Task PreCancelledCallsNeverReachRepository()
	{
		var repository = new Repository();
		var service = new BankTransactionAssignmentService(repository);
		using var source = new CancellationTokenSource();
		source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(TransactionId, source.Token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssignAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId, source.Token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReleaseAsync(TransactionId, BankTransactionConsumerType.Expense, ConsumerId, source.Token));
		Assert.Empty(repository.Calls);
	}

	private sealed class Repository : IBankTransactionAssignmentRepository
	{
		public bool Exists { get; init; } = true;
		public BankTransactionAssignment? Assignment { get; set; }
		public List<string> Calls { get; } = [];
		public string? FailureAt { get; init; }
		public Exception Failure { get; set; } = new InvalidOperationException();
		private void Call(string name, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Calls.Add(name);
			if (FailureAt == name) throw Failure;
		}
		public Task<BankTransactionAssignment?> GetAsync(Guid id, CancellationToken ct)
		{
			Call("get", ct);
			Assert.Equal(TransactionId, id);
			return Task.FromResult(Assignment);
		}
		public Task<bool> LockTransactionAsync(Guid id, CancellationToken ct)
		{
			Call("lock", ct);
			Assert.Equal(TransactionId, id);
			return Task.FromResult(Exists);
		}
		public Task TryAddAsync(Guid id, BankTransactionConsumerType type, Guid consumer, CancellationToken ct)
		{
			Call("insert", ct);
			Assignment ??= new() { BankTransactionId = id, ConsumerType = type, ConsumerId = consumer };
			return Task.CompletedTask;
		}
		public Task<bool> RemoveAsync(Guid id, BankTransactionConsumerType type, Guid consumer, CancellationToken ct)
		{
			Call("delete", ct);
			Assert.Equal((id, type, consumer), (Assignment!.BankTransactionId, Assignment.ConsumerType, Assignment.ConsumerId));
			Assignment = null;
			return Task.FromResult(true);
		}
	}
}
