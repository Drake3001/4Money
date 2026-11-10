using Microsoft.EntityFrameworkCore;

namespace ZPI4M.Grouping.Services.Transactions;

public interface IGroupingTransactionCoordinator
{
	void EnsureActive();
	T Reject<T>(T result);
	Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<T, bool> succeeded, CancellationToken cancellationToken = default);
	Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default);
	Task EnlistNotificationsAsync(CancellationToken cancellationToken);
	void AfterCommit(Func<Task> action);
	void AfterRollback(Func<Task> action);
}
