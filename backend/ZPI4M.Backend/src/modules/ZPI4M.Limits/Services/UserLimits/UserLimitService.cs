using System.Globalization;
using ZPI4M.Expense.Services.Categories;
using ZPI4M.Limits.Database;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Repositories;
using ZPI4M.Limits.Repositories.UserLimitRepository;
using ZPI4M.Limits.Services.Alerts;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.Results;
using ZPI4M.Limits.Services.Usage;
using ZPI4M.Limits.Services.Validation;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Limits.Services.UserLimits;

public sealed class UserLimitService(LimitsDbContext db, IUserLimitRepository limits, ICategoryLookupService categories, LimitWriteExecutor writes, LimitRequestService requests, LimitCalendar calendar, TimeProvider clock, LimitUsageService usage, LimitAlertService alerts) : IUserLimitService
{
	private UserLimitResponse Map(UserLimit value, DateOnly today)
	{
		var window = LimitCalendar.For(value, today);
		return new(value.Id, value.UserId, value.CategoryId, value.Amount.ToString("0.00", CultureInfo.InvariantCulture), value.CurrencyCode, value.Period, new DateTimeOffset(value.SetDate), value.IsEnabled, value.Version.ToString("D"), window.Start, window.End);
	}

	public Task<LimitsResult<UserLimitResponse>> CreateAsync(Guid actor, CreateUserLimitRequest request, CancellationToken token = default) => writes.ExecuteAsync<UserLimitResponse>(async ct =>
	{
		await alerts.LockOwnersAsync([actor], ct);
		var amount = requests.Amount(request.Amount);
		if (amount.Status != 200)
			return new(400, Errors: amount.Errors);
		var today = calendar.Today;
		var schedule = LimitRequestService.Schedule(request.Period, request.StartDate, request.EndDate, today);
		if (schedule.Count > 0)
			return new(400, Errors: schedule);
		var currency = request.CurrencyCode;
		if (currency != "PLN")
			return LimitRequestService.Invalid<UserLimitResponse>("currencyCode", "Only PLN budgets are supported.");
		if (request.CategoryId is Guid category && (category == Guid.Empty || !await categories.ExistsAsync(category, ct)))
			return LimitRequestService.Invalid<UserLimitResponse>("categoryId", "Category does not exist.");
		var value = new UserLimit
		{
			Id = Guid.NewGuid(),
			UserId = actor,
			CategoryId = request.CategoryId,
			Amount = amount.Value,
			CurrencyCode = currency,
			Period = request.Period,
			StartDate = request.StartDate,
			EndDate = request.EndDate,
			SetDate = clock.GetUtcNow().UtcDateTime
		};
		if (value.Period != LimitPeriod.Custom && !LimitOverlapPolicy.CanEnable(value, await limits.GetEnabledScopeAsync(actor, value.CategoryId, value.CurrencyCode, ct)))
			return new(409, Code: "OverlappingBudget");
		limits.Add(value);
		await db.SaveChangesAsync(ct);
		await alerts.EvaluateAsync(actor, value, today, ct);
		return new(201, Map(value, today));
	}, token);
	public Task<LimitsResult<UserLimitResponse>> UpdateAsync(Guid actor, Guid limitId, UpdateUserLimitRequest request, CancellationToken token = default) => ChangeAsync(actor, limitId, request.Version, request.Amount, null, token);
	public Task<LimitsResult<UserLimitResponse>> SetEnabledAsync(Guid actor, Guid limitId, SetLimitEnabledRequest request, CancellationToken token = default) => ChangeAsync(actor, limitId, request.Version, null, request.IsEnabled, token);
	private Task<LimitsResult<UserLimitResponse>> ChangeAsync(Guid actor, Guid limitId, string version, string? amount, bool? enabled, CancellationToken token) => writes.ExecuteAsync<UserLimitResponse>(async ct =>
	{
		await alerts.LockOwnersAsync([actor], ct);
		var value = await limits.GetAsync(actor, limitId, true, ct);
		if (value is null)
			return new(404);
		if (!LimitRequestService.ValidVersion(version, out var expected))
			return LimitRequestService.Invalid<UserLimitResponse>("version", "Use a nonempty UUID version token.");
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
			return LimitRequestService.Invalid<UserLimitResponse>("amount", "An amount is required.");
		if (enabled == true && value.Period != LimitPeriod.Custom)
		{
			if (!LimitOverlapPolicy.CanEnable(value, await limits.GetEnabledScopeAsync(actor, value.CategoryId, value.CurrencyCode, ct)))
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
	public async Task<LimitsResult<UserLimitResponse>> GetAsync(Guid actor, Guid limitId, CancellationToken token = default)
	{
		var value = await limits.GetAsync(actor, limitId, false, token);
		if (value is null)
			return new(404);
		var today = calendar.Today;
		var calculated = await usage.ForUserAsync(actor, value, today, token);
		return calculated.Status == 200 ? new(200, Map(value, today) with { Usage = calculated.Value }) : new(calculated.Status, Errors: calculated.Errors);
	}

	public async Task<LimitsResult<PagedResult<UserLimitResponse>>> ListAsync(Guid actor, LimitListRequest query, CancellationToken token = default)
	{
		var errors = requests.ValidateList(query);
		if (errors.Count > 0)
			return new(400, Errors: errors);
		var page = await limits.GetPageAsync(actor, new LimitQuery { IncludeDisabled = query.IncludeDisabled, CategoryId = query.CategoryId, CurrencyCode = query.CurrencyCode, Period = query.Period, PageNumber = query.PageNumber, PageSize = query.PageSize, Descending = query.Descending }, token);
		var today = calendar.Today;
		return new(200, new(page.Items.Select(v => Map(v, today)).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}
}
