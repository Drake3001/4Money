using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Notifications.Services.Notifications;

public sealed class NotificationService(
	INotificationRepository notifications,
	INotificationRecipientRepository recipients,
	NotificationsDbContext dbContext,
	IUserLookupService users) : INotificationService
{
	public Task<bool> MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
	{
		return recipients.MarkAsReadAsync(userId, notificationId, DateTime.UtcNow, cancellationToken);
	}

	public Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		return recipients.MarkAllAsReadAsync(userId, DateTime.UtcNow, cancellationToken);
	}

	public async Task<PagedResult<NotificationResponse>> GetByUserAsync(Guid userId, int pageNumber = 1, int pageSize = 20, bool? isRead = null, CancellationToken cancellationToken = default)
	{
		var page = await recipients.GetPageAsync(userId, pageNumber, pageSize, isRead, cancellationToken);
		var items = page.Items.Select(recipient =>
		{
			var notification = recipient.FkNotification;
			return new NotificationResponse(
				notification.Id, notification.Title, notification.Text, notification.Type,
				notification.CreatedAt, notification.FkGroupId, notification.FkExpenseId, recipient.ReadAt);
		}).ToArray();

		return new PagedResult<NotificationResponse>(items, page.TotalCount, page.PageNumber, page.PageSize);
	}

	public async Task<Guid?> NotifyAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
		ArgumentNullException.ThrowIfNull(request.RecipientIds);
		if (!Enum.IsDefined(request.Type))
			throw new ArgumentException("Unknown notification type.", nameof(request));
		if (request.GroupId == Guid.Empty || request.ExpenseId == Guid.Empty)
			throw new ArgumentException("Notification references must not be empty identifiers.", nameof(request));
		if (request.GroupId.HasValue && request.ExpenseId.HasValue)
			throw new ArgumentException("A notification can reference either a group or an expense, not both.", nameof(request));

		var recipientIds = request.RecipientIds.Distinct().ToArray();
		if (recipientIds.Contains(Guid.Empty))
			throw new ArgumentException("Recipient identifiers must not be empty.", nameof(request));

		cancellationToken.ThrowIfCancellationRequested();
		if (recipientIds.Length == 0)
			return null;
		var activeUserIds = await users.GetActiveUserIdsAsync(recipientIds, cancellationToken);
		if (activeUserIds.Count == 0)
			return null;

		var notification = new Notification
		{
			Id = Guid.NewGuid(),
			Type = request.Type,
			Title = request.Title.Trim(),
			Text = request.Text.Trim(),
			FkGroupId = request.GroupId,
			FkExpenseId = request.ExpenseId,
			CreatedAt = DateTime.UtcNow
		};
		foreach (var userId in activeUserIds)
		{
			notification.NotificationRecipients.Add(new NotificationRecipient
			{
				FkNotificationId = notification.Id,
				FkUserId = userId,
				FkNotification = notification,
				ReadAt = null
			});
		}

		notifications.Add(notification);
		await dbContext.SaveChangesAsync(cancellationToken);
		return notification.Id;
	}
}
