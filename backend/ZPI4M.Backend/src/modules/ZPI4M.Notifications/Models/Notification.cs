using System;
using System.Collections.Generic;

namespace ZPI4M.Notifications.Models;

public enum NotificationType
{
	General = 0,
	ExpenseAdded = 1,
	LimitApproaching = 2,
	LimitExceeded = 3,
	GroupMemberJoined = 4,
	SettlementReceived = 5
}

public partial class Notification
{
	public Guid Id { get; set; }

	public Guid? FkGroupId { get; set; }

	public Guid? FkExpenseId { get; set; }

	public string Title { get; set; } = null!;

	public string Text { get; set; } = null!;

	public NotificationType Type { get; set; }

	public DateTime CreatedAt { get; set; }

	public virtual ICollection<NotificationRecipient> NotificationRecipients { get; set; } = new List<NotificationRecipient>();
}
