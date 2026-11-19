using System.Globalization;
using Microsoft.Extensions.Options;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories.LimitAlertRepository;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Limits.Services.Usage;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;

namespace ZPI4M.Limits.Services.Alerts;

public sealed class LimitAlertService(LimitsDbContext db, ExpenseDbContext expenseDb, ILimitAlertRepository limits, LimitUsageService usage, LimitCalendar calendar, IGroupExpenseMetadataLookup groups, IGroupMemberLookup members, ICategoryLookupService categories, INotificationService notifications, IGroupingTransactionCoordinator transactions, IOptions<LimitsAlertsOptions> options, TimeProvider clock) : IExpenseChangeHandler
{
	private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
	public async Task LockOwnersAsync(IEnumerable<Guid> owners, CancellationToken token)
	{
		transactions.EnsureActive();
		await transactions.EnlistAsync(db, token);
		await transactions.EnlistAsync(expenseDb, token);
		await limits.LockOwnersAsync(owners, token);
	}

	public async Task HandleAsync(Guid actor, ExpenseDetails? previous, ExpenseDetails? current, CancellationToken cancellationToken = default)
	{
		var changes = new[]
		{
			previous,
			current
		}.OfType<ExpenseDetails>().ToArray();
		var owners = changes.SelectMany(v => v.Shares.Select(s => s.UserId)).Distinct().ToArray();
		await LockOwnersAsync(owners, cancellationToken);
		var today = calendar.Today;
		foreach (var limit in await limits.GetUsersAsync(owners, cancellationToken))
			if (changes.Any(v => v.Shares.Any(s => s.UserId == limit.UserId) && Affects(v, limit, today)))
				await EvaluateAsync(actor, limit, today, cancellationToken);
		// Expense holds the group lock before taking the ordered personal owner locks.
		foreach (var limit in await limits.GetGroupsAsync(changes.Select(v => v.GroupId).Distinct().ToArray(), cancellationToken))
			if (changes.Any(v => v.GroupId == limit.GroupId && Affects(v, limit, today)))
				await EvaluateAsync(actor, limit, today, cancellationToken);
	}

	private static bool Affects(ExpenseDetails value, ILimitDefinition limit, DateOnly today) => LimitCalendar.For(limit, today).Contains(LimitCalendar.Day(value.TransactionTime)) && (!limit.CategoryId.HasValue || value.Items.Any(i => i.CategoryId == limit.CategoryId));
	public Task<bool> EvaluateAsync(Guid actor, UserLimit limit, DateOnly today, CancellationToken token) => EvaluateCoreAsync(actor, limit, today, token);
	public Task<bool> EvaluateAsync(Guid actor, GroupLimit limit, DateOnly today, CancellationToken token) => EvaluateCoreAsync(actor, limit, today, token);
	public Task<bool> EvaluateSystemAsync(GroupLimit limit, DateOnly today, CancellationToken token) => EvaluateCoreAsync(null, limit, today, token);
	private async Task<bool> EvaluateCoreAsync(Guid? actor, ILimitDefinition limit, DateOnly today, CancellationToken token)
	{
		transactions.EnsureActive();
		await transactions.EnlistAsync(db, token);
		await transactions.EnlistAsync(expenseDb, token);
		if (limit is UserLimit user)
			await LockOwnersAsync([user.UserId], token);
		if (!limit.IsEnabled)
			return false;
		var window = LimitCalendar.For(limit, today);
		if (!window.Contains(today))
			return false;
		var state = await limits.GetStateAsync(limit, window.Key, token);
		if (state?.ExceededNotifiedAt is not null)
			return false;
		var read = limit switch
		{
			UserLimit personal => await usage.ForUserAsync(personal.UserId, personal, today, token),
			GroupLimit group when actor is Guid id => await usage.ForGroupAsync(id, group, today, token),
			GroupLimit group => await usage.ForGroupSystemAsync(group, today, token),
			_ => throw new InvalidOperationException("Unknown limit definition.")
		};
		var spent = GetSpent(read);
		var type = SelectType(spent, limit.Amount, options.Value.WarningThresholdPercent, state?.ApproachingHandledAt is not null);
		if (type is null)
			return false;
		IReadOnlyList<Guid> recipients;
		Guid? groupId = null;
		string? groupName = null;
		if (limit is GroupLimit grouped)
		{
			var metadata = await groups.GetAsync(grouped.GroupId, token);
			if (metadata.Status != 200 || metadata.Value is null)
				throw new InvalidOperationException("Limit group metadata unavailable.");
			recipients = await members.GetCurrentUserIdsAsync(grouped.GroupId, token);
			groupId = grouped.GroupId;
			groupName = metadata.Value.Name;
		}
		else
			recipients = [((UserLimit)limit).UserId];
		if (await NotifyAsync(type.Value, spent, limit.Amount, limit.CategoryId, window.Start, window.End, recipients, groupId, groupName, token) is null)
			return false;
		state ??= limits.AddState(limit, window.Key);
		var now = clock.GetUtcNow().UtcDateTime;
		state.ApproachingHandledAt ??= now;
		if (type == NotificationType.LimitExceeded)
			state.ExceededNotifiedAt = now;
		await db.SaveChangesAsync(token);
		return true;
	}

	// Expense totals are exact sums of money rounded to cents by the shared Expense calculator.
	private static decimal GetSpent(LimitsResult<LimitUsageResponse> result) => result.Status == 200 && result.Value is { SpentAmount: var amount } ? decimal.Parse(amount, CultureInfo.InvariantCulture) : throw new InvalidOperationException("Limit usage calculation failed.");
	public static NotificationType? SelectType(decimal spent, decimal amount, decimal threshold, bool approachingHandled) => spent > amount ? NotificationType.LimitExceeded : !approachingHandled && spent >= amount * threshold / 100m ? NotificationType.LimitApproaching : null;
	private async Task<Guid?> NotifyAsync(NotificationType type, decimal spent, decimal amount, Guid? categoryId, DateOnly start, DateOnly end, IReadOnlyList<Guid> recipients, Guid? groupId, string? groupName, CancellationToken token)
	{
		var category = categoryId is Guid id ? await categories.GetNameAsync(id, token) ?? throw new InvalidOperationException("Limit category unavailable.") : "wszystkie kategorie";
		var scope = groupId.HasValue ? $"Limit grupy „{groupName}”" : "Limit osobisty";
		var percent = decimal.Round(spent / amount * 100m, 2, MidpointRounding.AwayFromZero);
		var period = $"okres {start:dd.MM.yyyy}–{end:dd.MM.yyyy}";
		var text = $"{scope}, {category}, {period}: wykorzystano {spent.ToString("F2", Polish)} PLN z {amount.ToString("F2", Polish)} PLN ({percent.ToString("0.##", Polish)}%).";
		await transactions.EnlistNotificationsAsync(token);
		return await notifications.NotifyAsync(new CreateNotificationRequest(type, type == NotificationType.LimitExceeded ? "Przekroczono limit" : "Zbliżasz się do limitu", text, recipients, GroupId: groupId), token);
	}
}
