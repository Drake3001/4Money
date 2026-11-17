using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Banking.DTOs;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.ApiControllers;

[ApiController]
[AllowAnonymous]
[TypeFilter(typeof(BankingDatabaseExceptionFilter))]
[Route("api/banking/tink/callback")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TinkCallbackController(IBankingCallbackService callbacks) : ControllerBase
{
	[HttpGet]
	[ProducesResponseType(typeof(BankConnectionAttemptResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
	public async Task<ActionResult<BankConnectionAttemptResponse>> CompleteAsync(
	 [FromQuery] string? state, [FromQuery(Name = "credentials_id")] string? credentialsId,
	 [FromQuery] string? error, [FromQuery(Name = "error_reason")] string? errorReason,
	 CancellationToken cancellationToken)
	{
		Response.Headers["Referrer-Policy"] = "no-referrer";
		if (Request.Query.Any(pair => pair.Value.Count != 1))
		{
			var invalidPage = BankingBrowserResponse.Create(Request, false, StatusCodes.Status400BadRequest);
			if (invalidPage is not null) return invalidPage;
			return Problem(statusCode: 400, title: "Invalid banking callback.");
		}
		var result = await callbacks.CompleteAsync(new BankingCallback
		{
			State = state ?? "",
			CredentialsId = credentialsId,
			Error = error,
			ErrorReason = errorReason
		}, cancellationToken);
		if (!result.IsSuccess)
		{
			var status = result.Error switch
			{
				BankingErrorCode.InvalidCallback => StatusCodes.Status400BadRequest,
				BankingErrorCode.Conflict => StatusCodes.Status409Conflict,
				_ => StatusCodes.Status503ServiceUnavailable
			};
			if (status == 503)
				Response.Headers.RetryAfter = "5";
			var failurePage = BankingBrowserResponse.Create(Request, false, status);
			if (failurePage is not null) return failurePage;
			return Problem(statusCode: status, title: "The banking callback could not be completed.",
			 extensions: new Dictionary<string, object?>
			 {
				 ["code"] = status switch { 400 => "BANKING_INVALID_CALLBACK", 409 => "BANKING_CONFLICT", _ => "BANKING_TEMPORARILY_UNAVAILABLE" },
				 ["attemptId"] = result.AttemptId
			 });
		}
		var value = result.Value!;
		var page = BankingBrowserResponse.Create(Request, value.Status == BankConnectionAttemptStatus.Completed);
		if (page is not null) return page;
		return Ok(new BankConnectionAttemptResponse(value.Id, value.Status, value.ConnectionId,
		 value.CreatedAt, value.ExpiresAt, value.CompletedAt, value.FailureCode));
	}
}
