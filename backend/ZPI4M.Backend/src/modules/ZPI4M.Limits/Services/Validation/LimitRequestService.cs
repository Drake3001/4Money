using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Results;

namespace ZPI4M.Limits.Services.Validation;

public sealed class LimitRequestService
{
	public LimitsResult<decimal> Amount(string? amount)
	{
		if (amount is null || !Regex.IsMatch(amount, @"\A[0-9]{1,16}(\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || !decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
			return new(400, Errors: new Dictionary<string, string[]> { ["amount"] = ["Use a positive decimal string with at most 16 integer and two fractional digits."] });
		return new(200, parsed);
	}

	public static IReadOnlyDictionary<string, string[]> Schedule(LimitPeriod period, DateOnly? start, DateOnly? end, DateOnly today)
	{
		var errors = new Dictionary<string, string[]>();
		if (!Enum.IsDefined(period))
			errors["period"] = ["Use Week=1, Month=2, Year=3 or Custom=4."];
		if (period == LimitPeriod.Custom)
		{
			if (!start.HasValue || start == DateOnly.MinValue || start == DateOnly.MaxValue)
				errors["startDate"] = ["A finite start date is required."];
			if (!end.HasValue || end == DateOnly.MinValue || end == DateOnly.MaxValue)
				errors["endDate"] = ["A finite end date is required."];
			if (start > end)
				errors["endDate"] = ["End date must not precede start date."];
			if (end < today)
				errors["endDate"] = ["End date must be today or later."];
		}
		else
		{
			if (start.HasValue)
				errors["startDate"] = ["Calendar budgets must not specify dates."];
			if (end.HasValue)
				errors["endDate"] = ["Calendar budgets must not specify dates."];
		}

		return errors;
	}

	public static bool ValidVersion(string? text, out Guid value) => Guid.TryParseExact(text, "D", out value) && value != Guid.Empty;
	public static LimitsResult<T> Invalid<T>(string field, string message) => new(400, Errors: new Dictionary<string, string[]> { [field] = [message] });
	public IReadOnlyDictionary<string, string[]> ValidateList(LimitListRequest query)
	{
		var results = new List<ValidationResult>();
		Validator.TryValidateObject(query, new ValidationContext(query), results, true);
		var errors = results.SelectMany(r => r.MemberNames.DefaultIfEmpty("request").Select(n => (Name: char.ToLowerInvariant(n[0]) + n[1..], Message: r.ErrorMessage!))).GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.Select(r => r.Message).ToArray());
		if (query.CategoryId == Guid.Empty)
			errors["categoryId"] = ["Category ID must be nonempty."];
		if (query.Period.HasValue && !Enum.IsDefined(query.Period.Value))
			errors["period"] = ["Unknown period."];
		if (query.CurrencyCode is not null && query.CurrencyCode != "PLN")
			errors["currencyCode"] = ["Only PLN is supported."];
		return errors;
	}
}
