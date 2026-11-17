using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Grouping.Services.Expenses;

/// <summary>Notifies members after Expense saves a new expense in the shared transaction.</summary>
public sealed class GroupExpenseNotificationService(IGroupMemberRepository members,
	INotificationService notifications, IGroupingTransactionCoordinator transactions, IUserDisplayNameLookup names) : IGroupExpenseNotificationService
{
	public Task NotifyAddedAsync(Guid actor, Guid expenseId, string expenseName, decimal amount,
		GroupExpenseMetadata group, CancellationToken cancellationToken = default)
	{
		transactions.EnsureActive();
		return transactions.ExecuteAsync(async token =>
		{
			var recipients = await members.GetActiveUserIdsAsync(group.Id, actor, token);
			if (recipients.Count == 0) return true;
			await transactions.EnlistNotificationsAsync(token);
			var labels = await names.GetAsync([actor], token);
			var author = labels.GetValueOrDefault(actor, "Użytkownik");
			await notifications.NotifyAsync(new CreateNotificationRequest(NotificationType.ExpenseAdded,
				"Nowy wydatek", $"{author} dodał(a) wydatek w grupie {group.Name}.",
				recipients, ExpenseId: expenseId), token);
			return true;
		}, success => success, cancellationToken);
	}
}
