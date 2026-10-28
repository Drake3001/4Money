using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.DTOs;

public sealed record CreateNotificationRequest(
	NotificationType Type,
	string Title,
	string Text,
	IReadOnlyCollection<Guid> RecipientIds,
	Guid? GroupId = null,
	Guid? ExpenseId = null);
