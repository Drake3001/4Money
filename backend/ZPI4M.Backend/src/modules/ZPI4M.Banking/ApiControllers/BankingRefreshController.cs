using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.DTOs;
using ZPI4M.Banking.Repositories.BankRefreshRepository;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Banking.ApiControllers;

[ApiController]
[Authorize]
[Route("api/banking/refreshes")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BankingRefreshController(BankRefreshService service, ICurrentUser currentUser) : ControllerBase
{
	[HttpPost]
	[ProducesResponseType(typeof(StartBankRefreshResponse), StatusCodes.Status202Accepted)]
	public async Task<ActionResult<StartBankRefreshResponse>> StartAsync(CancellationToken ct)
	{
		if (!TryUser(out var userId)) return Error(401, "BANKING_UNAUTHENTICATED");
		try
		{
			var job = await service.StartAsync(userId, ct);
			if (job is null) return Error(409, "BANKING_TINK_USER_REQUIRED");
			return AcceptedAtRoute("Banking.GetRefresh", new { refreshId = job.Id }, new StartBankRefreshResponse(job.Id, job.Status, job.CreatedAt));
		}
		catch (RefreshIdentityException) { return Error(403, "BANKING_FORBIDDEN"); }
		catch (Exception error) when (error is DbException or DbUpdateException) { return Error(503, "BANKING_TEMPORARILY_UNAVAILABLE"); }
	}

	[HttpGet("{refreshId:guid}", Name = "Banking.GetRefresh")]
	[ProducesResponseType(typeof(BankRefreshResponse), StatusCodes.Status200OK)]
	public async Task<ActionResult<BankRefreshResponse>> GetAsync(Guid refreshId, CancellationToken ct)
	{
		if (!TryUser(out var userId)) return Error(401, "BANKING_UNAUTHENTICATED");
		try
		{
			var job = await service.GetAsync(userId, refreshId, ct);
			if (job is null) return Error(404, "BANKING_NOT_FOUND");
			var results = job.Accounts.OrderBy(x => x.AccountId).Select(x => new BankRefreshAccountResponse(x.AccountId,
				x.Status, x.AddedTransactions, x.UpdatedTransactions, x.LastSuccessfulSyncAt, x.FailureCode)).ToArray();
			return Ok(new BankRefreshResponse(job.Id, job.Status, job.CreatedAt, job.StartedAt, job.CompletedAt,
				results.Length, results.Count(x => x.Status == "SUCCEEDED"), results.Count(x => x.Status == "FAILED"), job.FailureCode, results));
		}
		catch (Exception error) when (error is DbException or DbUpdateException) { return Error(503, "BANKING_TEMPORARILY_UNAVAILABLE"); }
	}

	private bool TryUser(out Guid id)
	{
		id = currentUser.UserId ?? Guid.Empty;
		return User.Identity?.IsAuthenticated == true && id != Guid.Empty;
	}

	private ObjectResult Error(int status, string code) => Problem(statusCode: status,
		title: "The banking refresh operation could not be completed.", extensions: new Dictionary<string, object?> { ["code"] = code });
}
