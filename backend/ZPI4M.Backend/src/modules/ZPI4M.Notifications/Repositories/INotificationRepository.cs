using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.Repositories;

public interface INotificationRepository
{
	void Add(Notification entity);
	Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
