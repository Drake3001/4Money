using System.Text.Json;
using Xunit;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.DTOs.UserLimits;
using ZPI4M.Limits.Models;
using ZPI4M.Limits.Services.Calendar;
using ZPI4M.Limits.Services.Validation;

namespace ZPI4M.Limits.UnitTests;

public sealed class CalendarBudgetTests
{
	[Theory]
	[InlineData(1, "2026-11-16", "2026-11-16", "2026-11-22")]
	[InlineData(1, "2026-11-15", "2026-11-09", "2026-11-15")]
	[InlineData(1, "2027-01-01", "2026-12-28", "2027-01-03")]
	[InlineData(2, "2024-02-29", "2024-02-01", "2024-02-29")]
	[InlineData(2, "2025-02-28", "2025-02-01", "2025-02-28")]
	[InlineData(3, "2024-02-29", "2024-01-01", "2024-12-31")]
	public void CalendarWindows(int period, string day, string start, string end)
	{
		var window = LimitCalendar.For((LimitPeriod)period, DateOnly.Parse(day));
		Assert.Equal(DateOnly.Parse(start), window.Start);
		Assert.Equal(DateOnly.Parse(end), window.End);
	}

	[Theory]
	[InlineData("2026-03-29T00:30:00Z", "2026-03-29")]
	[InlineData("2026-03-29T22:30:00Z", "2026-03-30")]
	[InlineData("2026-10-25T22:30:00Z", "2026-10-25")]
	[InlineData("2026-10-25T23:30:00Z", "2026-10-26")]
	[InlineData("2026-12-31T23:00:00Z", "2027-01-01")]
	public void WarsawDates(string instant, string expected) => Assert.Equal(DateOnly.Parse(expected), new LimitCalendar(new Clock(instant)).Today);
	[Theory]
	[InlineData("2026-03-29T23:30:00Z", "2026-03-29")]
	[InlineData("2026-10-25T23:30:00Z", "2026-10-25")]
	[InlineData("2026-12-31T23:30:00Z", "2026-12-31")]
	public void ExpenseDayRetainsUtcDate(string instant, string expected) =>
		Assert.Equal(DateOnly.Parse(expected), LimitCalendar.Day(DateTimeOffset.Parse(instant).UtcDateTime));

	[Fact]
	public void CustomHasFixedWindowAndStartKey()
	{
		var limit = new UserLimit
		{
			Period = LimitPeriod.Custom,
			StartDate = new(2026, 3, 25),
			EndDate = new(2026, 4, 10)
		};
		var value = LimitCalendar.For(limit, new(2027, 1, 1));
		Assert.Equal(new DateOnly(2026, 3, 25), value.Start);
		Assert.Equal(new DateOnly(2026, 4, 10), value.End);
		Assert.Equal(value.Start, value.Key);
	}

	[Theory]
	[InlineData(4, "2026-03-25", "2026-04-10", true)]
	[InlineData(4, "2026-04-10", "2026-04-10", true)]
	[InlineData(4, "2026-04-11", "2026-04-10", false)]
	[InlineData(4, "2026-03-25", "2026-04-09", false)]
	[InlineData(4, null, "2026-04-10", false)]
	[InlineData(4, "2026-03-25", null, false)]
	[InlineData(4, "0001-01-01", "2026-04-10", false)]
	[InlineData(4, "2026-04-10", "9999-12-31", false)]
	[InlineData(2, "2026-04-01", "2026-04-30", false)]
	[InlineData(2, null, null, true)]
	[InlineData(99, null, null, false)]
	[InlineData(4, null, null, false)]
	public void ScheduleValidation(int period, string? from, string? to, bool valid)
	{
		var result = LimitRequestService.Schedule((LimitPeriod)period, from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), new(2026, 4, 10));
		Assert.Equal(valid, result.Count == 0);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.001")]
	[InlineData("1,00")]
	[InlineData("10000000000000000")]
	[InlineData(null)]
	public void InvalidAmounts(string? amount) => Assert.Equal(400, new LimitRequestService().Amount(amount).Status);
	[Theory]
	[InlineData("0.01")]
	[InlineData("9999999999999999.99")]
	public void ValidAmounts(string amount) => Assert.Equal(200, new LimitRequestService().Amount(amount).Status);
	[Theory]
	[InlineData("{\"amount\":\"100\",\"currencyCode\":\"PLN\",\"period\":2,\"previousLimitId\":null}")]
	[InlineData("{\"amount\":\"100\",\"currencyCode\":\"PLN\",\"period\":2,\"autoRenew\":false}")]
	public void ObsoleteCreateFieldsAreRejected(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateUserLimitRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
	[Theory]
	[InlineData("{}")]
	[InlineData("{\"isEnabled\":true}")]
	[InlineData("{\"version\":\"x\"}")]
	public void EnabledRequestRequiresBothFields(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SetLimitEnabledRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
	private sealed class Clock(string instant) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse(instant);
	}
}
