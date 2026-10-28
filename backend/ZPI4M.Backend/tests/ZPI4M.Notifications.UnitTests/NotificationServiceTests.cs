using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Notifications.UnitTests;

public sealed class NotificationServiceTests
{
	[Fact]
	public async Task NotifyCreatesUniqueUnreadRecipientsWithUtcTimeInOneSave()
	{
		using var db = new RecordingContext();
		var users = ActiveUsers();
		var service = CreateService(db, users);
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		var expense = Guid.NewGuid();
		var before = DateTime.UtcNow;

		var id = await service.NotifyAsync(new(NotificationType.ExpenseAdded, " Nowy wydatek ", " Zakupy ",
			[first, second, first], ExpenseId: expense));

		var notification = Assert.Single(db.ChangeTracker.Entries<Notification>()).Entity;
		Assert.NotEqual(Guid.Empty, id);
		Assert.Equal(id, notification.Id);
		Assert.Equal(NotificationType.ExpenseAdded, notification.Type);
		Assert.Equal("Nowy wydatek", notification.Title);
		Assert.Equal("Zakupy", notification.Text);
		Assert.Equal(expense, notification.FkExpenseId);
		Assert.Null(notification.FkGroupId);
		Assert.Equal(DateTimeKind.Utc, notification.CreatedAt.Kind);
		Assert.InRange(notification.CreatedAt, before, DateTime.UtcNow);
		Assert.Equal(new[] { first, second }, notification.NotificationRecipients.Select(r => r.FkUserId));
		Assert.All(notification.NotificationRecipients, r =>
		{
			Assert.Equal(id, r.FkNotificationId);
			Assert.Same(notification, r.FkNotification);
			Assert.Null(r.ReadAt);
		});
		Assert.Equal(1, ((RepositoryProxy)(object)users).Calls);
		Assert.Equal(1, db.Saves);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	[InlineData(8)]
	[InlineData(9)]
	public async Task InvalidRequestsFailBeforeDatabaseWrites(int invalidCase)
	{
		using var db = new RecordingContext();
		var users = ActiveUsers();
		var service = CreateService(db, users);
		CreateNotificationRequest request = new(NotificationType.General, "Tytul", "Tresc", [Guid.NewGuid()]);
		request = invalidCase switch
		{
			0 => request with { Title = " " },
			1 => request with { Text = " " },
			2 => request with { Type = (NotificationType)99 },
			3 => request with { RecipientIds = [] },
			4 => request with { RecipientIds = [Guid.Empty] },
			5 => request with { GroupId = Guid.Empty },
			6 => request with { ExpenseId = Guid.Empty },
			7 => request with { GroupId = Guid.NewGuid(), ExpenseId = Guid.NewGuid() },
			8 => request with { RecipientIds = null! },
			_ => null!
		};

		await Assert.ThrowsAnyAsync<ArgumentException>(() => service.NotifyAsync(request));
		Assert.Equal(0, ((RepositoryProxy)(object)users).Calls);
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Equal(0, db.Saves);
	}

	[Fact]
	public async Task MissingActiveRecipientIsSkipped()
	{
		using var db = new RecordingContext();
		var invalidUserId = Guid.NewGuid();
		var users = RepositoryProxy.Create<IUserLookupService>((_, args) =>
		{
			var ids = (IReadOnlyCollection<Guid>)args[0]!;
			return Task.FromResult<IReadOnlyCollection<Guid>>(ids.Where(id => id != invalidUserId).ToArray());
		});
		var service = CreateService(db, users);

		var activeUserId = Guid.NewGuid();
		var id = await service.NotifyAsync(new(NotificationType.General, "Tytul", "Tresc", [activeUserId, invalidUserId]));
		Assert.NotNull(id);
		var notification = Assert.Single(db.ChangeTracker.Entries<Notification>()).Entity;
		Assert.Equal(activeUserId, Assert.Single(notification.NotificationRecipients).FkUserId);
		Assert.Equal(1, db.Saves);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NoActiveRecipientsReturnsNullWithoutWrites(bool emptyInput)
	{
		using var db = new RecordingContext();
		var users = RepositoryProxy.Create<IUserLookupService>((_, _) =>
			Task.FromResult<IReadOnlyCollection<Guid>>(Array.Empty<Guid>()));
		var service = CreateService(db, users);
		var id = await service.NotifyAsync(new(NotificationType.General, "T", "T", emptyInput ? [] : [Guid.NewGuid()]));
		Assert.Null(id);
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Equal(0, db.Saves);
		Assert.Equal(emptyInput ? 0 : 1, ((RepositoryProxy)(object)users).Calls);
	}

	[Fact]
	public async Task CancelledNotifyDoesNotReadOrWrite()
	{
		using var db = new RecordingContext();
		var users = ActiveUsers();
		var service = CreateService(db, users);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.NotifyAsync(
			new(NotificationType.General, "Tytul", "Tresc", [Guid.NewGuid()]), new CancellationToken(true)));
		Assert.Equal(0, ((RepositoryProxy)(object)users).Calls);
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Equal(0, db.Saves);
	}

	[Fact]
	public async Task InboxMapsContentAndRecipientReadStateAndPreservesPaging()
	{
		using var db = new RecordingContext();
		var userId = Guid.NewGuid();
		using var cancellation = new CancellationTokenSource();
		var notification = new Notification
		{
			Id = Guid.NewGuid(),
			Title = "Tytul",
			Text = "Tresc",
			Type = NotificationType.GroupMemberJoined,
			CreatedAt = DateTime.UtcNow,
			FkGroupId = Guid.NewGuid()
		};
		var readAt = DateTime.UtcNow;
		var recipients = RepositoryProxy.Create<INotificationRecipientRepository>((method, args) =>
		{
			Assert.Equal(nameof(INotificationRecipientRepository.GetPageAsync), method.Name);
			Assert.Equal(new object?[] { userId, 2, 1, true, cancellation.Token }, args);
			return Task.FromResult(new PagedResult<NotificationRecipient>([
				new() { FkUserId = userId, FkNotificationId = notification.Id, FkNotification = notification, ReadAt = readAt }
			], 3, 2, 1));
		});
		var page = await CreateService(db, ActiveUsers(), recipients).GetByUserAsync(userId, 2, 1, true, cancellation.Token);
		Assert.Equal(new NotificationResponse(notification.Id, "Tytul", "Tresc", notification.Type,
			notification.CreatedAt, notification.FkGroupId, null, readAt), Assert.Single(page.Items));
		Assert.True(page.Items[0].IsRead);
		Assert.Equal(3, page.TotalCount);
		Assert.Equal(2, page.PageNumber);
		Assert.Equal(1, page.PageSize);
		Assert.Equal(0, db.Saves);
		Assert.False(new NotificationResponse(Guid.NewGuid(), "T", "T", NotificationType.General, DateTime.UtcNow, null, null, null).IsRead);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MarkReadPassesOwnerAndUtcTimeAndReturnsRepositoryResult(bool found)
	{
		using var db = new RecordingContext();
		var userId = Guid.NewGuid();
		var notificationId = Guid.NewGuid();
		using var cancellation = new CancellationTokenSource();
		var before = DateTime.UtcNow;
		var recipients = RepositoryProxy.Create<INotificationRecipientRepository>((method, args) =>
		{
			Assert.Equal(userId, args[0]);
			var timestampIndex = method.Name == nameof(INotificationRecipientRepository.MarkAsReadAsync) ? 2 : 1;
			if (timestampIndex == 2)
				Assert.Equal(notificationId, args[1]);
			var readAt = Assert.IsType<DateTime>(args[timestampIndex]);
			Assert.Equal(DateTimeKind.Utc, readAt.Kind);
			Assert.InRange(readAt, before, DateTime.UtcNow);
			Assert.Equal(cancellation.Token, args[timestampIndex + 1]);
			return timestampIndex == 2 ? Task.FromResult(found) : Task.CompletedTask;
		});
		var service = CreateService(db, ActiveUsers(), recipients);
		Assert.Equal(found, await service.MarkAsReadAsync(userId, notificationId, cancellation.Token));
		await service.MarkAllAsReadAsync(userId, cancellation.Token);
		Assert.Equal(0, db.Saves);
	}

	private static IUserLookupService ActiveUsers() => RepositoryProxy.Create<IUserLookupService>((method, args) =>
	{
		Assert.Equal(nameof(IUserLookupService.GetActiveUserIdsAsync), method.Name);
		var ids = (IReadOnlyCollection<Guid>)args[0]!;
		Assert.Equal(ids.Count, ids.Distinct().Count());
		return Task.FromResult<IReadOnlyCollection<Guid>>(ids.ToArray());
	});

	private static NotificationService CreateService(RecordingContext db, IUserLookupService users, INotificationRecipientRepository? recipients = null) =>
		new(new NotificationRepository(db), recipients ?? RepositoryProxy.Create<INotificationRecipientRepository>((_, _) => throw new InvalidOperationException("Unexpected repository call.")), db, users);

	private sealed class RecordingContext() : NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
		.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options)
	{
		public int Saves { get; private set; }
		public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Saves++;
			return Task.FromResult(ChangeTracker.Entries().Count());
		}
	}

	public class RepositoryProxy : DispatchProxy
	{
		private Func<MethodInfo, object?[], object?> _handler = null!;
		public int Calls { get; private set; }
		public static T Create<T>(Func<MethodInfo, object?[], object?> handler) where T : class
		{
			var proxy = Create<T, RepositoryProxy>();
			((RepositoryProxy)(object)proxy)._handler = handler;
			return proxy;
		}
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			Calls++;
			return _handler(targetMethod!, args!);
		}
	}
}
