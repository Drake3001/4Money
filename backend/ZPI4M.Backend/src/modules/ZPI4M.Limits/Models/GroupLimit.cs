namespace ZPI4M.Limits.Models;

public enum LimitPeriod
{
	Week = 1,
	Month = 2,
	Year = 3,
	Custom = 4
}

public class GroupLimit : ILimitDefinition
{
	public Guid Id { get; set; }
	public Guid GroupId { get; set; }
	public Guid? CategoryId { get; set; }
	public decimal Amount { get; set; }
	public string CurrencyCode { get; set; } = "PLN";
	public LimitPeriod Period { get; set; }
	public DateOnly? StartDate { get; set; }
	public DateOnly? EndDate { get; set; }
	public DateTime SetDate { get; set; }
	public bool IsEnabled { get; set; } = true;
	public Guid Version { get; set; } = Guid.NewGuid();
}
