using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Database;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Notifications.IntegrationTests;

public sealed class NotificationFlowTests : PostgresFixture
{
	[PostgresFact]
	public async Task ActiveUserLookupReturnsOnlyRequestedActiveIdsWithoutTracking()
	{
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		var lookup = new UserLookupService(users);
		var ids = await lookup.GetActiveUserIdsAsync([AnnaId, AnnaId, InactiveId, Guid.NewGuid()]);

		Assert.Equal(AnnaId, Assert.Single(ids));
		Assert.Empty(users.ChangeTracker.Entries());
		Assert.Empty(await lookup.GetActiveUserIdsAsync([]));
	}

	[PostgresFact]
	public async Task NotifyPersistsContentAndUniqueUnreadRecipientsForEveryType()
	{
		foreach (var type in Enum.GetValues<NotificationType>())
		{
			var id = await NotifyAsync(new(type, " Tytul ", " Tresc ", [AnnaId, BartekId, AnnaId]));
			await using var db = OpenDatabase();
			var notification = await db.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.Id == id);
			Assert.Equal(type, notification.Type);
			Assert.Equal("Tytul", notification.Title);
			Assert.Equal("Tresc", notification.Text);
			Assert.Equal(DateTimeKind.Utc, notification.CreatedAt.Kind);
			Assert.Equal(2, notification.NotificationRecipients.Count);
			Assert.All(notification.NotificationRecipients, r => Assert.Null(r.ReadAt));
			Assert.Equal(new[] { AnnaId, BartekId }, notification.NotificationRecipients.OrderBy(r => r.FkUserId).Select(r => r.FkUserId));
		}
	}

	[PostgresFact]
	public async Task NoActiveRecipientsAndForeignKeyFailureLeaveNoPartialNotification()
	{
		await using var before = OpenDatabase();
		var notificationCount = await before.Notifications.CountAsync();
		var recipientCount = await before.NotificationRecipients.CountAsync();
		foreach (var invalidId in new[] { InactiveId, Guid.NewGuid() })
			Assert.Null(await TryNotifyAsync(new(NotificationType.General, "T", "T", [invalidId])));
		// A real database failure exercises rollback of the entire graph, not only input validation.
		await Assert.ThrowsAsync<DbUpdateException>(() => NotifyAsync(
			new(NotificationType.ExpenseAdded, "T", "T", [AnnaId, BartekId], ExpenseId: Guid.NewGuid())));
		await using var after = OpenDatabase();
		Assert.Equal(notificationCount, await after.Notifications.CountAsync());
		Assert.Equal(recipientCount, await after.NotificationRecipients.CountAsync());
	}

	[PostgresFact]
	public async Task NotifySkipsInactiveAndMissingRecipients()
	{
		var id = await NotifyAsync(new(NotificationType.General, "T", "T", [AnnaId, InactiveId, Guid.NewGuid(), AnnaId]));
		await using var db = OpenDatabase();
		var recipients = await db.NotificationRecipients.Where(r => r.FkNotificationId == id).ToArrayAsync();
		Assert.Equal(AnnaId, Assert.Single(recipients).FkUserId);
	}

	[PostgresFact]
	public async Task InboxIsScopedFilteredPagedAndDoesNotMarkEntriesRead()
	{
		await using var db = OpenDatabase();
		var repository = new NotificationRecipientRepository(db);
		var first = await repository.GetPageAsync(AnnaId, pageSize: 1);
		Assert.Equal(3, first.TotalCount);
		Assert.Equal(GeneralNotificationId, Assert.Single(first.Items).FkNotificationId);
		Assert.Null(first.Items[0].ReadAt);
		Assert.True(first.HasNextPage);
		Assert.Equal(FirstNotificationId, Assert.Single((await repository.GetPageAsync(AnnaId, 2, 1)).Items).FkNotificationId);
		Assert.Equal(GeneralNotificationId, Assert.Single((await repository.GetPageAsync(AnnaId, isRead: false)).Items).FkNotificationId);
		var readPage = await repository.GetPageAsync(AnnaId, isRead: true);
		Assert.Equal(2, readPage.TotalCount);
		Assert.Equal(FirstNotificationId, readPage.Items[0].FkNotificationId);
		Assert.Equal(NotificationType.GroupMemberJoined, readPage.Items[1].FkNotification.Type);
		Assert.All(first.Items, r => Assert.Equal(AnnaId, r.FkUserId));
		Assert.All(first.Items, r => Assert.All(r.FkNotification.NotificationRecipients, other => Assert.Equal(AnnaId, other.FkUserId)));
		Assert.Empty(db.ChangeTracker.Entries());
		Assert.Empty((await repository.GetPageAsync(AnnaId, 10, 1)).Items);
		var empty = await repository.GetPageAsync(Guid.NewGuid());
		Assert.Equal(0, empty.TotalCount);
		Assert.Empty(empty.Items);
		Assert.Null(await db.NotificationRecipients.Where(r => r.FkNotificationId == GeneralNotificationId && r.FkUserId == AnnaId).Select(r => r.ReadAt).SingleAsync());
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetPageAsync(AnnaId, pageSize: 101));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetPageAsync(AnnaId, int.MaxValue, 100));
	}

	[PostgresFact]
	public async Task EqualCreationTimesHaveStableOrderingAcrossPages()
	{
		var low = Guid.Parse("91000000-0000-0000-0000-000000000001");
		var high = Guid.Parse("91000000-0000-0000-0000-000000000002");
		await using var db = OpenDatabase();
		var createdAt = DateTime.UtcNow.AddHours(1);
		foreach (var id in new[] { low, high })
			db.Notifications.Add(new Notification
			{
				Id = id,
				Title = "T",
				Text = "T",
				CreatedAt = createdAt,
				NotificationRecipients = [new() { FkNotificationId = id, FkUserId = AnnaId }]
			});
		await db.SaveChangesAsync();
		db.ChangeTracker.Clear();
		var repository = new NotificationRecipientRepository(db);
		Assert.Equal(high, Assert.Single((await repository.GetPageAsync(AnnaId, 1, 1)).Items).FkNotificationId);
		Assert.Equal(low, Assert.Single((await repository.GetPageAsync(AnnaId, 2, 1)).Items).FkNotificationId);
	}

	[PostgresFact]
	public async Task MarkReadIsIndependentPerRecipientIdempotentAndRejectsForeignEntries()
	{
		var id = await NotifyAsync(new(NotificationType.General, "T", "T", [AnnaId, BartekId]));
		var firstRead = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		await using var db = OpenDatabase();
		var repository = new NotificationRecipientRepository(db);
		Assert.True(await repository.MarkAsReadAsync(AnnaId, id, firstRead));
		Assert.True(await repository.MarkAsReadAsync(AnnaId, id, firstRead.AddDays(1)));
		Assert.False(await repository.MarkAsReadAsync(CelinaId, id, firstRead));
		Assert.False(await repository.MarkAsReadAsync(AnnaId, Guid.NewGuid(), firstRead));
		var recipients = await db.NotificationRecipients.Where(r => r.FkNotificationId == id).ToListAsync();
		Assert.Equal(firstRead, recipients.Single(r => r.FkUserId == AnnaId).ReadAt);
		Assert.Null(recipients.Single(r => r.FkUserId == BartekId).ReadAt);
	}

	[PostgresFact]
	public async Task MarkAllCoversAllPagesPreservesPreviousDatesAndDoesNotAffectOtherUsersOrFutureEntries()
	{
		await using var db = OpenDatabase();
		var oldRead = await db.NotificationRecipients.AsNoTracking().Where(r => r.FkUserId == AnnaId && r.FkNotificationId == FirstNotificationId).Select(r => r.ReadAt).SingleAsync();
		var repository = new NotificationRecipientRepository(db);
		var readAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		await repository.MarkAllAsReadAsync(AnnaId, readAt);
		await repository.MarkAllAsReadAsync(AnnaId, readAt.AddDays(1));
		Assert.Equal(3, (await repository.GetPageAsync(AnnaId, isRead: true)).TotalCount);
		Assert.Empty((await repository.GetPageAsync(AnnaId, isRead: false)).Items);
		Assert.Equal(oldRead, await db.NotificationRecipients.Where(r => r.FkUserId == AnnaId && r.FkNotificationId == FirstNotificationId).Select(r => r.ReadAt).SingleAsync());
		Assert.Equal(readAt, await db.NotificationRecipients.Where(r => r.FkUserId == AnnaId && r.FkNotificationId == GeneralNotificationId).Select(r => r.ReadAt).SingleAsync());
		Assert.Equal(2, (await repository.GetPageAsync(BartekId, isRead: false)).TotalCount);
		await repository.MarkAllAsReadAsync(Guid.NewGuid(), readAt);
		var newId = await NotifyAsync(new(NotificationType.General, "Nowe", "T", [AnnaId]));
		Assert.Equal(newId, Assert.Single((await repository.GetPageAsync(AnnaId, isRead: false)).Items).FkNotificationId);
	}

	[PostgresFact]
	public async Task ConcurrentSingleAndBulkReadPreserveWinningDate()
	{
		var id = await NotifyAsync(new(NotificationType.General, "T", "T", [AnnaId, BartekId]));
		var first = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		var second = first.AddHours(1);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async Task ReadAsync(bool all)
		{
			await using var db = OpenDatabase();
			var repository = new NotificationRecipientRepository(db);
			await gate.Task;
			if (all)
				await repository.MarkAllAsReadAsync(AnnaId, second);
			else
				Assert.True(await repository.MarkAsReadAsync(AnnaId, id, first));
		}
		var operations = new[] { ReadAsync(false), ReadAsync(true), ReadAsync(false) };
		gate.SetResult();
		await Task.WhenAll(operations);
		await using var check = OpenDatabase();
		var readAt = await check.NotificationRecipients.Where(r => r.FkNotificationId == id && r.FkUserId == AnnaId).Select(r => r.ReadAt).SingleAsync();
		Assert.Contains(readAt, new DateTime?[] { first, second });
		Assert.True(await new NotificationRecipientRepository(check).MarkAsReadAsync(AnnaId, id, second.AddDays(1)));
		Assert.Equal(readAt, await check.NotificationRecipients.Where(r => r.FkNotificationId == id && r.FkUserId == AnnaId).Select(r => r.ReadAt).SingleAsync());
		Assert.Null(await check.NotificationRecipients.Where(r => r.FkNotificationId == id && r.FkUserId == BartekId).Select(r => r.ReadAt).SingleAsync());
	}

	[PostgresFact]
	public async Task HttpInboxAndReadActionsUseRealJwtSessionAndPersistChanges()
	{
		await using var host = await StartHostAsync();
		await AuthenticateAsync(host, AnnaId);
		using var list = await host.Client.GetAsync($"/api/notifications?userId={BartekId}&pageSize=1&isRead=false");
		Assert.Equal(HttpStatusCode.OK, list.StatusCode);
		var page = await list.Content.ReadFromJsonAsync<PagedResult<NotificationResponse>>();
		Assert.Equal(GeneralNotificationId, Assert.Single(page!.Items).Id);
		Assert.False(page.Items[0].IsRead);
		using var foreign = await host.Client.PutAsync("/api/notifications/90000000-0000-0000-0000-000000000002/read", null);
		Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
		using var read = await host.Client.PutAsync($"/api/notifications/{GeneralNotificationId}/read", null);
		Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
		using var repeated = await host.Client.PutAsync($"/api/notifications/{GeneralNotificationId}/read", null);
		Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
		using var all = await host.Client.PutAsync("/api/notifications/read-all", null);
		Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
		var unread = await host.Client.GetFromJsonAsync<PagedResult<NotificationResponse>>("/api/notifications?isRead=false");
		Assert.Empty(unread!.Items);
		await using var db = OpenDatabase();
		Assert.Equal(3, await db.NotificationRecipients.CountAsync(r => r.FkUserId == AnnaId && r.ReadAt != null));
		Assert.Equal(2, await db.NotificationRecipients.CountAsync(r => r.FkUserId == BartekId && r.ReadAt == null));
	}

	[PostgresFact]
	public async Task InvalidRevokedAndInactiveSessionsCannotReadOrUpdateInbox()
	{
		await using var host = await StartHostAsync();
		using var anonymous = await host.Client.GetAsync("/api/notifications");
		Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
		var sessionId = await AuthenticateAsync(host, AnnaId);
		await using var users = new UserDbContext(new DbContextOptionsBuilder<UserDbContext>().UseNpgsql(ConnectionString).Options);
		await users.UserTokens.Where(t => t.Id == sessionId).ExecuteDeleteAsync();
		using var revoked = await host.Client.PutAsync("/api/notifications/read-all", null);
		Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
		await AuthenticateAsync(host, InactiveId);
		using var inactive = await host.Client.GetAsync("/api/notifications");
		Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
		await using var db = OpenDatabase();
		Assert.Null(await db.NotificationRecipients.Where(r => r.FkUserId == AnnaId && r.FkNotificationId == GeneralNotificationId).Select(r => r.ReadAt).SingleAsync());
	}

	[PostgresFact]
	public async Task DatabaseEnforcesRecipientUniquenessAndCascadesNotificationDeletion()
	{
		await using (var db = OpenDatabase())
		{
			db.NotificationRecipients.Add(new() { FkNotificationId = FirstNotificationId, FkUserId = AnnaId });
			await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
		}
		await using var check = OpenDatabase();
		await check.Notifications.Where(n => n.Id == FirstNotificationId).ExecuteDeleteAsync();
		Assert.False(await check.NotificationRecipients.AnyAsync(r => r.FkNotificationId == FirstNotificationId));
	}

	[PostgresFact]
	public async Task DeletingExpensePreservesNotificationAndClearsItsReference()
	{
		var expenseId = Guid.Parse("70000000-0000-0000-0000-000000000003");
		var notificationId = await NotifyAsync(new(NotificationType.ExpenseAdded, "Wydatek", "T", [AnnaId], ExpenseId: expenseId));
		await using var connection = new Npgsql.NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		// Remove dependent expense rows first; do not weaken unrelated database constraints.
		await using var command = new Npgsql.NpgsqlCommand("""
			DELETE FROM expense.expense_share WHERE fk_expense_id = @id;
			DELETE FROM expense.expense_item WHERE fk_expense_id = @id;
			DELETE FROM expense.expense WHERE _id = @id;
			""", connection);
		command.Parameters.AddWithValue("id", expenseId);
		await command.ExecuteNonQueryAsync();
		await using var db = OpenDatabase();
		Assert.Null((await db.Notifications.SingleAsync(n => n.Id == notificationId)).FkExpenseId);
		Assert.True(await db.NotificationRecipients.AnyAsync(r => r.FkNotificationId == notificationId && r.FkUserId == AnnaId));
	}
}
