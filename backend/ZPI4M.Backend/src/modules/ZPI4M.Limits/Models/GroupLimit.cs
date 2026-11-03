namespace ZPI4M.Limits.Models;

public enum LimitPeriod { Week = 1, Month = 2, Year = 3 }

public class GroupLimit
{
	public Guid Id { get; set; }
	public Guid GroupId { get; set; }
	public LimitPeriod Period { get; set; }
	public decimal Amount { get; set; }
}
