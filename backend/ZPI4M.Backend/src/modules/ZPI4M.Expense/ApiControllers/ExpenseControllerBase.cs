using System.Data.Common;
using System.Text.Json;
using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Expense.ApiControllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public abstract class ExpenseControllerBase(ICurrentUser currentUser) : ControllerBase
{
	protected async Task<IActionResult> ExecuteAsync<T>(Func<Guid, Task<ExpenseResult<T>>> operation, Func<T, IActionResult> success) where T : notnull
	{
		if (currentUser.UserId is not Guid userId || userId == Guid.Empty)
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated user is unavailable.");
		try
		{
			var result = await operation(userId);
			if (result.IsSuccess) return success(result.Value!);
			if (result.Error == ExpenseErrorCode.InvalidExpense)
			{
				foreach (var error in result.ValidationErrors)
					foreach (var field in error.MemberNames.DefaultIfEmpty(string.Empty))
						ModelState.AddModelError(string.Join('.', field.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)),
							error.ErrorMessage ?? "Invalid expense.");
				if (ModelState.IsValid) ModelState.AddModelError(string.Empty, "Invalid expense.");
				return ValidationProblem(ModelState);
			}
			return result.Error switch
			{
				ExpenseErrorCode.NotFound => Problem(statusCode: 404, title: "Resource not found."),
				ExpenseErrorCode.Forbidden => Problem(statusCode: 403, title: "Operation forbidden."),
				ExpenseErrorCode.Conflict => Problem(statusCode: 409, title: "Expense conflicts with current data."),
				ExpenseErrorCode.PayloadTooLarge => Problem(statusCode: 413, title: "Receipt file is too large."),
				ExpenseErrorCode.UnsupportedReceiptFormat => Problem(statusCode: 415, title: "Unsupported receipt format."),
				ExpenseErrorCode.UnprocessableReceipt => Problem(statusCode: 422, title: "Receipt cannot be processed."),
				ExpenseErrorCode.OcrFailure => Problem(statusCode: 502, title: "Receipt analysis failed."),
				ExpenseErrorCode.OcrTimeout => Problem(statusCode: 504, title: "Receipt analysis timed out."),
				_ => Problem(statusCode: 503, title: "Expense service is temporarily unavailable.")
			};
		}
		catch (Exception error) when (error is DbException or DbUpdateException or AmazonS3Exception or IOException or HttpRequestException)
		{
			return Problem(statusCode: 503, title: "Expense service is temporarily unavailable.");
		}
	}

}
