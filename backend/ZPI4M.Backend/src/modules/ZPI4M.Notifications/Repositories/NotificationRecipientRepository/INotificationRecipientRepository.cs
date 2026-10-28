using ZPI4M.Notifications.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Notifications.Repositories.NotificationRecipientRepository;

public interface INotificationRecipientRepository
{
	Task<PagedResult<NotificationRecipient>> GetPageAsync(Guid userId, int pageNumber = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default);
	Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, DateTime readAt, CancellationToken cancellationToken = default);
	Task MarkAllAsReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken = default);
}
