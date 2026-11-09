using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ZPI4M.Banking.ApiControllers;

/// <summary>Translates connection persistence failures without exposing database diagnostics.</summary>
public sealed class BankingDatabaseExceptionFilter(ProblemDetailsFactory problems) : IExceptionFilter
{
	public void OnException(ExceptionContext context)
	{
		if (context.Exception is not (DbException or DbUpdateException))
			return;

		var response = context.HttpContext.Response;
		response.Headers.CacheControl = "no-store,no-cache";
		response.Headers.Pragma = "no-cache";
		if (context.ActionDescriptor is ControllerActionDescriptor action
			&& action.ControllerTypeInfo.AsType() == typeof(TinkCallbackController))
		{
			response.Headers["Referrer-Policy"] = "no-referrer";
			response.Headers.RetryAfter = "5";
		}
		var problem = problems.CreateProblemDetails(context.HttpContext,
			StatusCodes.Status503ServiceUnavailable, title: "The banking operation is temporarily unavailable.");
		problem.Extensions["code"] = "BANKING_TEMPORARILY_UNAVAILABLE";
		problem.Extensions["attemptId"] = null;
		context.Result = new ObjectResult(problem)
		{
			StatusCode = StatusCodes.Status503ServiceUnavailable,
			ContentTypes = { "application/problem+json" }
		};
		context.ExceptionHandled = true;
	}
}
