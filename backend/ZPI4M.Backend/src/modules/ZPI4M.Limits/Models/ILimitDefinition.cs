namespace ZPI4M.Limits.Models;

public interface ILimitDefinition
{
	Guid Id { get; set; }

	Guid? CategoryId { get; set; }

	decimal Amount { get; set; }

	string CurrencyCode { get; set; }

	LimitPeriod Period { get; set; }

	DateOnly? StartDate { get; set; }

	DateOnly? EndDate { get; set; }

	DateTime SetDate { get; set; }

	bool IsEnabled { get; set; }

	Guid Version { get; set; }
}
