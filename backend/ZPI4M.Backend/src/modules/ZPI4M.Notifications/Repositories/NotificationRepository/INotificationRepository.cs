using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.Repositories.NotificationRepository;

public interface INotificationRepository
{
	void Add(Notification entity);
	Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
