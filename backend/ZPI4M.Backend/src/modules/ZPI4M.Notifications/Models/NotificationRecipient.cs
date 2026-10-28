namespace ZPI4M.Notifications.Models;

public class NotificationRecipient
{
	public Guid FkNotificationId { get; set; }

	public Guid FkUserId { get; set; }

	public DateTime? ReadAt { get; set; }

	public virtual Notification FkNotification { get; set; } = null!;
}
