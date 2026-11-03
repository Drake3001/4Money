using ZPI4M.Grouping.Services.Transactions;
public sealed class ValidationOnlyTransactions : IGroupingTransactionCoordinator
{
	public void EnsureActive() => throw new NotSupportedException();
	public T Reject<T>(T result) => result;
	public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<T, bool> succeeded, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Validation should reject before database access.");
	public Task EnlistNotificationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
	public void AfterCommit(Func<Task> action) => throw new NotSupportedException();
	public void AfterRollback(Func<Task> action) => throw new NotSupportedException();
}
