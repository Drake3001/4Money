namespace ZPI4M.Limits.Models;

public class GroupLimitAlertState : ILimitAlertState
{
	public Guid LimitId { get; set; }
	public DateOnly PeriodStartDate { get; set; }
	public DateTime? ApproachingHandledAt { get; set; }
	public DateTime? ExceededNotifiedAt { get; set; }
}
