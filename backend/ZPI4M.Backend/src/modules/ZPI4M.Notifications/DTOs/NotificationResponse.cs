using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.DTOs;

public sealed record NotificationResponse(
	Guid Id,
	string Title,
	string Text,
	NotificationType Type,
	DateTime CreatedAt,
	Guid? GroupId,
	Guid? ExpenseId,
	DateTime? ReadAt)
{
	public bool IsRead => ReadAt.HasValue;
}
