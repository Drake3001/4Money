namespace ZPI4M.Limits.Models;

public class UserLimit : ILimitDefinition
{
	public Guid Id { get; set; }
	public Guid UserId { get; set; }
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
