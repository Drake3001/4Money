using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Services.Calendar;

public sealed record LimitWindow(DateOnly Start, DateOnly End)
{
	public DateOnly Key => Start;

	public bool Contains(DateOnly day) => day >= Start && day <= End;
}

public sealed class LimitCalendar(TimeProvider clock)
{
	private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
	public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Warsaw).DateTime);

	public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc);
	public static LimitWindow For(ILimitDefinition limit, DateOnly today) => limit.Period == LimitPeriod.Custom ? new(limit.StartDate ?? throw new InvalidOperationException("Custom budget start unavailable."), limit.EndDate ?? throw new InvalidOperationException("Custom budget end unavailable.")) : For(limit.Period, today);
	public LimitWindow Current(LimitPeriod period) => For(period, Today);
	public static LimitWindow For(LimitPeriod period, DateOnly today) => period switch
	{
		LimitPeriod.Week => Week(today),
		LimitPeriod.Month => new(new(today.Year, today.Month, 1), new(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
		LimitPeriod.Year => new(new(today.Year, 1, 1), new(today.Year, 12, 31)),
		_ => throw new ArgumentOutOfRangeException(nameof(period))
	};
	private static LimitWindow Week(DateOnly day)
	{
		var start = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
		return new(start, DateOnly.FromDayNumber(Math.Min(start.DayNumber + 6, DateOnly.MaxValue.DayNumber)));
	}
}
