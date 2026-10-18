using Microsoft.EntityFrameworkCore;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Models;

namespace ZPI4M.Notifications.Repositories;

public class NotificationRepository : INotificationRepository
{
	private readonly NotificationsDbContext _context;

	public NotificationRepository(NotificationsDbContext context)
	{
		_context = context;
	}

	public void Add(Notification entity)
	{
		_context.Notifications.Add(entity);
	}

	public Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default)
	{
		return _context.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId, cancellationToken);
	}
}
