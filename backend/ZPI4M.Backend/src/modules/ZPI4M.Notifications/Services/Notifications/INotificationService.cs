using ZPI4M.Notifications.DTOs;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Notifications.Services.Notifications;

public interface INotificationService
{
	Task<Guid?> NotifyAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);
	Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
	Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<PagedResult<NotificationResponse>> GetByUserAsync(Guid userId, int pageNumber = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default);
}
