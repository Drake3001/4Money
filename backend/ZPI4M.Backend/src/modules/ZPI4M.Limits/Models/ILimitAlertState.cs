namespace ZPI4M.Limits.Models;

public interface ILimitAlertState
{
	Guid LimitId { get; set; }

	DateOnly PeriodStartDate { get; set; }

	DateTime? ApproachingHandledAt { get; set; }

	DateTime? ExceededNotifiedAt { get; set; }
}
