using System.Globalization;
using ZPI4M.Expense.Services.Aggregation;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Contracts.Aggregation;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.Results;

namespace ZPI4M.Limits.Services.Usage;

public sealed class LimitUsageService(IExpenseAggregationService expenses, IExpenseSystemAggregationService systemExpenses)
{
	public Task<LimitsResult<LimitUsageResponse>> ForUserAsync(Guid actor, UserLimit limit, DateOnly today, CancellationToken token = default) =>
		CalculateAsync(actor, limit, today, null, token);
	public Task<LimitsResult<LimitUsageResponse>> ForGroupAsync(Guid actor, GroupLimit limit, DateOnly today, CancellationToken token = default) =>
		CalculateAsync(actor, limit, today, limit.GroupId, token);
	public Task<LimitsResult<LimitUsageResponse>> ForGroupSystemAsync(GroupLimit limit, DateOnly today, CancellationToken token = default) =>
		CalculateAsync(null, limit, today, limit.GroupId, token);

	private async Task<LimitsResult<LimitUsageResponse>> CalculateAsync(Guid? actor, ILimitDefinition limit,
		DateOnly today, Guid? groupId, CancellationToken token)
	{
		if (limit.CurrencyCode != "PLN") return new(400, Errors: new Dictionary<string, string[]> { ["currencyCode"] = ["Only PLN is supported."] });
		var window = LimitCalendar.For(limit, today);
		var read = actor is Guid user
			? await expenses.GetUsageTotalAsync(user, window.Start, window.End,
				groupId.HasValue ? ExpenseAggregationScope.Group : ExpenseAggregationScope.Personal, groupId, limit.CategoryId, token)
			: await systemExpenses.GetGroupUsageTotalAsync(groupId!.Value, window.Start, window.End, limit.CategoryId, token);
		if (!read.IsSuccess)
		{
			if (read.Error is ExpenseErrorCode.NotFound or ExpenseErrorCode.Forbidden)
				return new(404);
			if (read.Error == ExpenseErrorCode.TemporarilyUnavailable)
				return new(503);
			if (read.Error == ExpenseErrorCode.InvalidExpense)
			{
				var errors = read.ValidationErrors.SelectMany(error => error.MemberNames.DefaultIfEmpty("request").Select(member => (Field: char.ToLowerInvariant(member[0]) + member[1..], Message: error.ErrorMessage ?? "Invalid expense filter."))).GroupBy(error => error.Field).ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
				return new(400, Errors: errors);
			}

			throw new InvalidOperationException("Expense totals could not be calculated.");
		}

		var totals = read.Value!;
		if (totals.CurrencyCode != limit.CurrencyCode)
			throw new InvalidOperationException("Expense totals currency does not match the budget currency.");
		var spent = totals.Amount;
		var amount = limit.Amount;
		return new(200, new(limit.CurrencyCode, spent.ToString("0.00", CultureInfo.InvariantCulture), (amount - spent).ToString("0.00", CultureInfo.InvariantCulture), decimal.Round(spent / amount * 100m, 2, MidpointRounding.AwayFromZero), spent > amount));
	}
}
