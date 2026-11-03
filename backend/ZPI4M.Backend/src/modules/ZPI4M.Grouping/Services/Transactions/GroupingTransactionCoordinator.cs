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
	private bool enlisted;
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
			if (!committed) await RollbackAsync(transaction, !commitAttempted);
			throw;
		}
		finally
		{
			if (failed) retired = true;
			active = null;
			// Detach before callbacks; cleanup errors must not replace the original failure or committed success.
			if (enlisted)
			{
				try { await inbox.Database.UseTransactionAsync(null, CancellationToken.None); }
				catch (Exception error) { retired = true; logger.LogWarning("Transaction detach failed ({ErrorType}).", error.GetType().Name); }
			}
			enlisted = false;
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
	public async Task EnlistNotificationsAsync(CancellationToken cancellationToken)
	{
		if (active is null || failed) throw new InvalidOperationException("An active Grouping unit of work is required.");
		if (!ReferenceEquals(db.Database.GetDbConnection(), inbox.Database.GetDbConnection()))
			throw new InvalidOperationException("Grouping and Notifications must share one scoped connection.");
		var existing = inbox.Database.CurrentTransaction;
		if (existing is not null && existing.GetDbTransaction() != active.GetDbTransaction())
			throw new InvalidOperationException("Notifications is enlisted in a different transaction.");
		if (existing is null) await inbox.Database.UseTransactionAsync(active.GetDbTransaction(), cancellationToken);
		enlisted = true;
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
