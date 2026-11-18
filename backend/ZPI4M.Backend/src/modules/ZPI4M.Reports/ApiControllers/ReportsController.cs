using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Reports.DTOs.CategoryBreakdown;
using ZPI4M.Reports.DTOs.DailyTotals;
using ZPI4M.Reports.DTOs.DayOfWeekTotals;
using ZPI4M.Reports.DTOs.PeriodCategoryTotals;
using ZPI4M.Reports.Services.Reports;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Reports.ApiControllers;

[ApiController]
[Authorize]
[Route("api/reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ReportsController(ICurrentUser currentUser, IReportsService reports) : ControllerBase
{
	[HttpGet("category-breakdown", Name = "getCategoryBreakdown")]
	[EndpointSummary("Get expense totals by category")]
	[ProducesResponseType(typeof(CategoryBreakdownResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetCategoryBreakdown([FromQuery] CategoryBreakdownRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => reports.GetCategoryBreakdownAsync(actor, request, cancellationToken));

	[HttpGet("period-category-totals", Name = "getExpenseTotalsByPeriodAndCategory")]
	[EndpointSummary("Get expense totals by calendar period and category")]
	[ProducesResponseType(typeof(ExpenseTotalsByPeriodAndCategoryResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetExpenseTotalsByPeriodAndCategory([FromQuery] ExpenseTotalsByPeriodAndCategoryRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => reports.GetExpenseTotalsByPeriodAndCategoryAsync(actor, request, cancellationToken));

	[HttpGet("day-of-week-totals", Name = "getExpenseTotalsByDayOfWeek")]
	[EndpointSummary("Get average daily expenses by day of week")]
	[ProducesResponseType(typeof(ExpenseTotalsByDayOfWeekResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetExpenseTotalsByDayOfWeek([FromQuery] ExpenseTotalsByDayOfWeekRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => reports.GetExpenseTotalsByDayOfWeekAsync(actor, request, cancellationToken));

	[HttpGet("daily-totals", Name = "getDailyTotals")]
	[EndpointSummary("Get expense totals for every date in the requested range")]
	[ProducesResponseType(typeof(DailyTotalsResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetDailyExpenseTotals([FromQuery] DailyTotalsRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => reports.GetDailyExpenseTotalsAsync(actor, request, cancellationToken));

	private async Task<IActionResult> ExecuteAsync<T>(Func<Guid, Task<T>> operation)
	{
		if (currentUser.UserId is not Guid actor || actor == Guid.Empty)
			return Problem(statusCode: 401, title: "Authenticated user is unavailable.");
		if (!Request.Query.TryGetValue("scope", out var scopes) || scopes.Count != 1 ||
			scopes[0] is not ("personal" or "group"))
			ModelState.AddModelError("scope", "Use personal or group exactly once.");
		if (Request.Query.TryGetValue("groupId", out var groups) &&
			(groups.Count != 1 || groups[0]?.Length != 36 || !Guid.TryParseExact(groups[0], "D", out var groupId) || groupId == Guid.Empty))
			ModelState.AddModelError("groupId", "Use a nonempty UUID or omit groupId.");
		foreach (var field in new[] { "dateFrom", "dateTo" })
		{
			if (Request.Query.TryGetValue(field, out var values) && (values.Count != 1 ||
				!DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
				ModelState.AddModelError(field, "Use a date in YYYY-MM-DD format.");
		}
		if (!ModelState.IsValid) return ValidationProblem(ModelState);
		try
		{
			return Ok(await operation(actor));
		}
		catch (ValidationException error)
		{
			return Invalid([error.ValidationResult ?? new ValidationResult(error.Message)]);
		}
		catch (ReportsReadException error)
		{
			return error.Error switch
			{
				ExpenseErrorCode.InvalidExpense => Invalid(error.ValidationErrors),
				ExpenseErrorCode.Forbidden => Problem(statusCode: 403, title: "Operation forbidden."),
				ExpenseErrorCode.NotFound => Problem(statusCode: 404, title: "Resource not found."),
				ExpenseErrorCode.Conflict => Problem(statusCode: 409, title: "Report conflicts with current data."),
				_ => Problem(statusCode: 503, title: "Reports are temporarily unavailable.")
			};
		}
		catch (Exception error) when (error is DbException or DbUpdateException or IOException or HttpRequestException)
		{
			return Problem(statusCode: 503, title: "Reports are temporarily unavailable.");
		}
	}

	private IActionResult Invalid(IEnumerable<ValidationResult> errors)
	{
		var modelState = new ModelStateDictionary();
		foreach (var error in errors)
			foreach (var member in error.MemberNames.DefaultIfEmpty(string.Empty))
			{
				modelState.AddModelError(JsonNamingPolicy.CamelCase.ConvertName(member), error.ErrorMessage ?? "Invalid report.");
			}
		if (modelState.IsValid) modelState.AddModelError(string.Empty, "Invalid report.");
		return ValidationProblem(modelState);
	}
}
