using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using ZPI4M.Grouping.Database;
using ZPI4M.Notifications.Database;

namespace ZPI4M.Grouping.Services.Transactions;

public sealed class GroupingTransactionCoordinator(GroupingDbContext db, NotificationsDbContext inbox,
 ILogger<GroupingTransactionCoordinator> logger) : IGroupingTransactionCoordinator
{
	private IDbContextTransaction? active;
	private bool failed;
	private bool retired;
	private readonly HashSet<DbContext> enlisted = [];
	private readonly List<Func<Task>> committedActions = [];
	private readonly List<Func<Task>> rollbackActions = [];

	public void EnsureActive() => RequireActive();
	public T Reject<T>(T result)
	{
		if (retired) throw new InvalidOperationException("Create a new DI scope after a failed Grouping operation.");
		if (active is not null) failed = true;
		return result;
	}
	public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, Func<T, bool> succeeded, CancellationToken cancellationToken = default)
	{
		if (retired) throw new InvalidOperationException("The failed Grouping unit of work must be disposed. Create a new DI scope.");
		if (active is not null)
		{
			if (failed) throw new InvalidOperationException("The Grouping transaction requires rollback.");
			try
			{
				var nested = await action(cancellationToken);
				if (!succeeded(nested)) failed = true;
				return nested;
			}
			catch { failed = true; throw; }
		}
		if (db.Database.CurrentTransaction is not null || inbox.Database.CurrentTransaction is not null)
			throw new InvalidOperationException("Start the unit of work through IGroupingTransactionCoordinator.");
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
		active = transaction;
		var commitAttempted = false;
		var committed = false;
		try
		{
			var result = await action(cancellationToken);
			if (!succeeded(result))
			{
				failed = true;
				await RollbackAsync(transaction);
				return result;
			}
			if (failed) throw new InvalidOperationException("A nested Grouping operation failed; the transaction cannot commit.");
			cancellationToken.ThrowIfCancellationRequested();
			commitAttempted = true;
			await transaction.CommitAsync(cancellationToken);
			committed = true;
			return result;
		}
		catch
		{
			retired = true;
			if (!committed)
			{
				if (commitAttempted && rollbackActions.Count > 0)
					logger.LogWarning("Commit outcome is unknown. Uploaded files were retained for reconciliation.");
				await RollbackAsync(transaction, !commitAttempted);
			}
			throw;
		}
		finally
		{
			if (failed) retired = true;
			active = null;
			// Detach before callbacks; cleanup errors must not replace the original failure or committed success.
			foreach (var context in enlisted)
			{
				try { await context.Database.UseTransactionAsync(null, CancellationToken.None); }
				catch (Exception error) { retired = true; logger.LogWarning("Transaction detach failed ({ErrorType}).", error.GetType().Name); }
			}
			enlisted.Clear();
			if (committed) await RunActionsAsync(committedActions);
			committedActions.Clear(); rollbackActions.Clear();
			failed = false;
		}
	}
	private async Task RollbackAsync(IDbContextTransaction transaction, bool cleanupFiles = true)
	{
		retired = true;
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		try
		{
			await transaction.RollbackAsync(timeout.Token);
			if (cleanupFiles) await RunActionsAsync(rollbackActions);
		}
		catch (Exception error) { logger.LogWarning("Grouping rollback failed ({ErrorType}).", error.GetType().Name); }
	}
	public Task EnlistNotificationsAsync(CancellationToken cancellationToken) => EnlistAsync(inbox, cancellationToken);

	public async Task EnlistAsync(DbContext context, CancellationToken cancellationToken = default)
	{
		RequireActive();
		try
		{
			ArgumentNullException.ThrowIfNull(context);
			if (!ReferenceEquals(db.Database.GetDbConnection(), context.Database.GetDbConnection()))
				throw new InvalidOperationException("Enlisted contexts must share one scoped connection.");
			var existing = context.Database.CurrentTransaction;
			if (existing is not null && existing.GetDbTransaction() != active!.GetDbTransaction())
				throw new InvalidOperationException("The context is enlisted in a different transaction.");
			if (ReferenceEquals(context, db)) return;
			if (existing is null) await context.Database.UseTransactionAsync(active!.GetDbTransaction(), cancellationToken);
			enlisted.Add(context);
		}
		catch { failed = true; throw; }
	}
	public void AfterCommit(Func<Task> action) { RequireActive(); committedActions.Add(action); }
	public void AfterRollback(Func<Task> action) { RequireActive(); rollbackActions.Add(action); }
	private void RequireActive()
	{
		if (active is null || retired || failed) throw new InvalidOperationException("An active Grouping unit of work is required.");
	}
	private async Task RunActionsAsync(IEnumerable<Func<Task>> actions)
	{
		foreach (var action in actions)
		{
			try { await action(); }
			catch (Exception error) { logger.LogWarning("Grouping file cleanup failed ({ErrorType}).", error.GetType().Name); }
		}
	}
}
