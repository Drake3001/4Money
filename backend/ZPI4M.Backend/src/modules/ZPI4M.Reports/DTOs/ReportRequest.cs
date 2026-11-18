using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ZPI4M.Expense.Services.Contracts.Aggregation;

namespace ZPI4M.Reports.DTOs;

public abstract class ReportRequest
{
	[BindRequired]
	[Description("Inclusive start date in YYYY-MM-DD format.")]
	public required DateOnly DateFrom { get; init; }

	[BindRequired]
	[Description("Inclusive end date in YYYY-MM-DD format.")]
	public required DateOnly DateTo { get; init; }

	[BindRequired]
	[Description("Required: personal or group. Personal requires no GroupId; Group requires a nonempty GroupId.")]
	public required ExpenseAggregationScope Scope { get; init; }

	public Guid? GroupId { get; init; }

	[FromQuery(Name = "categoryIds")]
	[Description("Optional. Repeat categoryIds=UUID for multiple categories. Omit for an empty selection and empty report. Values use standard ASP.NET Core Guid collection binding.")]
	public IReadOnlyList<Guid> CategoryIds { get; init; } = new List<Guid>();
}
