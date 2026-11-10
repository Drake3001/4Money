using System.Globalization;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;

namespace ZPI4M.Grouping.Services.Expenses;

/// <summary>Notifies members after Expense saves a new expense in the shared transaction.</summary>
public sealed class GroupExpenseNotificationService(IGroupMemberRepository members,
	INotificationService notifications, IGroupingTransactionCoordinator transactions) : IGroupExpenseNotificationService
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
			var formattedAmount = amount.ToString("F2", CultureInfo.GetCultureInfo("pl-PL"));
			await notifications.NotifyAsync(new CreateNotificationRequest(NotificationType.ExpenseAdded,
				"Nowy wydatek", $"Dodano wydatek „{expenseName}” za {formattedAmount} zł w grupie „{group.Name}”.",
				recipients, ExpenseId: expenseId), token);
			return true;
		}, success => success, cancellationToken);
	}
}
