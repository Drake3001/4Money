using Microsoft.EntityFrameworkCore;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Notifications.Repositories.NotificationRecipientRepository;

public class NotificationRecipientRepository : INotificationRecipientRepository
{
	private readonly NotificationsDbContext _context;

	public NotificationRecipientRepository(NotificationsDbContext context)
	{
		_context = context;
	}

	public async Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, DateTime readAt, CancellationToken cancellationToken = default)
	{
		var recipient = _context.NotificationRecipients
			.Where(r => r.FkUserId == userId && r.FkNotificationId == notificationId);

		var affectedRows = await recipient.Where(r => r.ReadAt == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ReadAt, readAt), cancellationToken);

		return affectedRows == 1 || await recipient.AnyAsync(cancellationToken);
	}

	public Task MarkAllAsReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken = default)
	{
		return _context.NotificationRecipients
			.Where(r => r.FkUserId == userId && r.ReadAt == null)
			.ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ReadAt, readAt), cancellationToken);
	}

	public async Task<PagedResult<NotificationRecipient>> GetPageAsync(Guid userId, int pageNumber = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default)
	{
		int offset = new PagedQuery { PageNumber = pageNumber, PageSize = pageSize }.GetOffset();

		var recipients = _context.NotificationRecipients.AsNoTracking()
			.Where(r => r.FkUserId == userId);
		if (isRead.HasValue)
			recipients = recipients.Where(r => r.ReadAt.HasValue == isRead.Value);

		int totalCount = await recipients.CountAsync(cancellationToken);
		var items = await recipients
			.OrderByDescending(r => r.FkNotification.CreatedAt)
			.ThenByDescending(r => r.FkNotificationId)
			.Include(r => r.FkNotification)
			.Skip(offset)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		return new PagedResult<NotificationRecipient>(items, totalCount, pageNumber, pageSize);
	}
}
