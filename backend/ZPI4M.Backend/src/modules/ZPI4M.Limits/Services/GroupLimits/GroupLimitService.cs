using System.Globalization;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Grouping.Services.Access;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.DTOs.GroupLimits;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories;
using ZPI4M.Limits.Repositories.GroupLimitRepository;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Limits.Services.Usage;
using ZPI4M.Limits.Services.Validation;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Services.GroupLimits;

public sealed class GroupLimitService(LimitsDbContext db, IGroupLimitRepository limits, IGroupAccessService access, ICategoryLookupService categories, LimitWriteExecutor writes, LimitRequestService requests, LimitCalendar calendar, TimeProvider clock, LimitUsageService usage, LimitAlertService alerts) : IGroupLimitService
{
	private GroupLimitResponse Map(GroupLimit value, DateOnly today)
	{
		var window = LimitCalendar.For(value, today);
		return new(value.Id, value.GroupId, value.CategoryId, value.Amount.ToString("0.00", CultureInfo.InvariantCulture), value.CurrencyCode, value.Period, new DateTimeOffset(value.SetDate), value.IsEnabled, value.Version.ToString("D"), window.Start, window.End);
	}

	public Task<LimitsResult<GroupLimitResponse>> CreateAsync(Guid actor, Guid groupId, CreateGroupLimitRequest request, CancellationToken token = default) => writes.ExecuteAsync<GroupLimitResponse>(async ct =>
	{
		var allowed = await access.LockForAdministratorAsync(actor, groupId, ct);
		if (allowed.Status != 200)
			return new(allowed.Status);
		var amount = requests.Amount(request.Amount);
		if (amount.Status != 200)
			return new(400, Errors: amount.Errors);
		var today = calendar.Today;
		var schedule = LimitRequestService.Schedule(request.Period, request.StartDate, request.EndDate, today);
		if (schedule.Count > 0)
			return new(400, Errors: schedule);
		var currency = allowed.Value!.CurrencyCode;
		if (currency != "PLN")
			return LimitRequestService.Invalid<GroupLimitResponse>("currencyCode", "Only PLN budgets are supported.");
		if (request.CategoryId is Guid category && (category == Guid.Empty || !await categories.ExistsAsync(category, ct)))
			return LimitRequestService.Invalid<GroupLimitResponse>("categoryId", "Category does not exist.");
		var value = new GroupLimit
		{
			Id = Guid.NewGuid(),
			GroupId = groupId,
			CategoryId = request.CategoryId,
			Amount = amount.Value,
			CurrencyCode = currency,
			Period = request.Period,
			StartDate = request.StartDate,
			EndDate = request.EndDate,
			SetDate = clock.GetUtcNow().UtcDateTime
		};
		if (value.Period != LimitPeriod.Custom && !LimitOverlapPolicy.CanEnable(value, await limits.GetEnabledScopeAsync(groupId, value.CategoryId, value.CurrencyCode, ct)))
			return new(409, Code: "OverlappingBudget");
		limits.Add(value);
		await db.SaveChangesAsync(ct);
		await alerts.EvaluateAsync(actor, value, today, ct);
		return new(201, Map(value, today));
	}, token);
	public Task<LimitsResult<GroupLimitResponse>> UpdateAsync(Guid actor, Guid groupId, Guid limitId, UpdateGroupLimitRequest request, CancellationToken token = default) => ChangeAsync(actor, groupId, limitId, request.Version, request.Amount, null, token);
	public Task<LimitsResult<GroupLimitResponse>> SetEnabledAsync(Guid actor, Guid groupId, Guid limitId, SetLimitEnabledRequest request, CancellationToken token = default) => ChangeAsync(actor, groupId, limitId, request.Version, null, request.IsEnabled, token);
	private Task<LimitsResult<GroupLimitResponse>> ChangeAsync(Guid actor, Guid groupId, Guid limitId, string version, string? amount, bool? enabled, CancellationToken token) => writes.ExecuteAsync<GroupLimitResponse>(async ct =>
	{
		var allowed = await access.LockForAdministratorAsync(actor, groupId, ct);
		if (allowed.Status != 200)
			return new(allowed.Status);
		var value = await limits.GetAsync(groupId, limitId, true, ct);
		if (value is null)
			return new(404);
		if (!LimitRequestService.ValidVersion(version, out var expected))
			return LimitRequestService.Invalid<GroupLimitResponse>("version", "Use a nonempty UUID version token.");
		if (value.Version != expected)
			return new(409, Code: "VersionConflict");
		var today = calendar.Today;
		if (amount is not null && value.Period == LimitPeriod.Custom && value.EndDate < today)
			return new(409, Code: "FinishedBudgetImmutable");
		if (amount is not null)
		{
			var parsed = requests.Amount(amount);
			if (parsed.Status != 200)
				return new(400, Errors: parsed.Errors);
			value.Amount = parsed.Value;
		}
		// JSON null is invalid even though the public property is a string.
		else if (!enabled.HasValue)
			return LimitRequestService.Invalid<GroupLimitResponse>("amount", "An amount is required.");
		if (enabled == true && value.Period != LimitPeriod.Custom)
		{
			if (!LimitOverlapPolicy.CanEnable(value, await limits.GetEnabledScopeAsync(groupId, value.CategoryId, value.CurrencyCode, ct)))
				return new(409, Code: "OverlappingBudget");
		}

		if (enabled.HasValue)
			value.IsEnabled = enabled.Value;
		value.Version = Guid.NewGuid();
		await db.SaveChangesAsync(ct);
		if (value.IsEnabled)
			await alerts.EvaluateAsync(actor, value, today, ct);
		return new(200, Map(value, today));
	}, token);
	public async Task<LimitsResult<GroupLimitResponse>> GetAsync(Guid actor, Guid groupId, Guid limitId, CancellationToken token = default)
	{
		var allowed = await access.GetForMemberAsync(actor, groupId, token);
		if (allowed.Status != 200)
			return new(allowed.Status);
		var value = await limits.GetAsync(groupId, limitId, false, token);
		if (value is null)
			return new(404);
		var today = calendar.Today;
		var calculated = await usage.ForGroupAsync(actor, value, today, token);
		return calculated.Status == 200 ? new(200, Map(value, today) with { Usage = calculated.Value }) : new(calculated.Status, Errors: calculated.Errors);
	}

	public async Task<LimitsResult<PagedResult<GroupLimitResponse>>> ListAsync(Guid actor, Guid groupId, LimitListRequest query, CancellationToken token = default)
	{
		var errors = requests.ValidateList(query);
		if (errors.Count > 0)
			return new(400, Errors: errors);
		var allowed = await access.GetForMemberAsync(actor, groupId, token);
		if (allowed.Status != 200)
			return new(allowed.Status);
		var page = await limits.GetPageAsync(groupId, new LimitQuery { IncludeDisabled = query.IncludeDisabled, CategoryId = query.CategoryId, CurrencyCode = query.CurrencyCode, Period = query.Period, PageNumber = query.PageNumber, PageSize = query.PageSize, Descending = query.Descending }, token);
		var today = calendar.Today;
		return new(200, new(page.Items.Select(v => Map(v, today)).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}
}
