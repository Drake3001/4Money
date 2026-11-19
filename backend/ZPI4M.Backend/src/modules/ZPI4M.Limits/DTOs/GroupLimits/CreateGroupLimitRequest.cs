using System.ComponentModel;
using System.Text.Json.Serialization;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.DTOs.GroupLimits;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateGroupLimitRequest
{
	[JsonRequired]
	public required string Amount { get; init; }
	public Guid? CategoryId { get; init; }

	[JsonRequired]
	public required LimitPeriod Period { get; init; }

	[Description("Required for Custom=4; inclusive Warsaw date. Omit for calendar budgets.")]
	public DateOnly? StartDate { get; init; }

	[Description("Required for Custom=4; inclusive Warsaw date, today or later at creation. Omit for calendar budgets.")]
	public DateOnly? EndDate { get; init; }
}
