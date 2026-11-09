using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZPI4M.Banking.DTOs;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Banking.ApiControllers;

[ApiController]
[Authorize]
[Route("api/banking")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(BankingDatabaseExceptionFilter))]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class BankingController(
	IBankingConnectionService connectionService,
	ICurrentUser currentUser) : ControllerBase
{
	[HttpPost("connection-attempts")]
	[ProducesResponseType(typeof(StartBankConnectionResponse), StatusCodes.Status201Created)]
	public async Task<ActionResult<StartBankConnectionResponse>> StartConnectionAsync(
		CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
			return InvalidIdentity();

		var result = await connectionService.StartConnectionAsync(userId, cancellationToken);
		if (!result.IsSuccess)
			return MapError(result.Error!.Value, result.AttemptId);

		var value = result.Value!;
		return CreatedAtRoute("Banking.GetConnectionAttempt", new { attemptId = value.AttemptId },
			new StartBankConnectionResponse(value.AttemptId, value.AuthorizationUrl, value.ExpiresAt));
	}

	[HttpGet("connection-attempts/{attemptId:guid}", Name = "Banking.GetConnectionAttempt")]
	[ProducesResponseType(typeof(BankConnectionAttemptResponse), StatusCodes.Status200OK)]
	public async Task<ActionResult<BankConnectionAttemptResponse>> GetConnectionAttemptAsync(
		Guid attemptId, CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
			return InvalidIdentity();

		var result = await connectionService.GetConnectionAttemptAsync(userId, attemptId, cancellationToken);
		if (!result.IsSuccess)
			return MapError(result.Error!.Value, result.AttemptId);

		var value = result.Value!;
		return Ok(new BankConnectionAttemptResponse(value.Id, value.Status, value.ConnectionId,
			value.CreatedAt, value.ExpiresAt, value.CompletedAt, value.FailureCode));
	}

	[HttpGet("connections")]
	[ProducesResponseType(typeof(IReadOnlyList<BankConnectionResponse>), StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<BankConnectionResponse>>> GetConnectionsAsync(
		CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
			return InvalidIdentity();

		var result = await connectionService.GetConnectionsAsync(userId, cancellationToken);
		if (!result.IsSuccess)
			return MapError(result.Error!.Value, result.AttemptId);

		return Ok(result.Value!.Select(MapConnection).ToArray());
	}

	[HttpGet("connections/{connectionId:guid}")]
	[ProducesResponseType(typeof(BankConnectionResponse), StatusCodes.Status200OK)]
	public async Task<ActionResult<BankConnectionResponse>> GetConnectionAsync(
		Guid connectionId, CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
			return InvalidIdentity();

		var result = await connectionService.GetConnectionAsync(userId, connectionId, cancellationToken);
		if (!result.IsSuccess)
			return MapError(result.Error!.Value, result.AttemptId);

		return Ok(MapConnection(result.Value!));
	}

	[HttpGet("consents")]
	[ProducesResponseType(typeof(IReadOnlyList<BankConsentResponse>), StatusCodes.Status200OK)]
	public async Task<ActionResult<IReadOnlyList<BankConsentResponse>>> GetConsentsAsync(CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
			return InvalidIdentity();

		var result = await connectionService.GetConsentsAsync(userId, cancellationToken);
		if (!result.IsSuccess)
			return MapError(result.Error!.Value, result.AttemptId);

		return Ok(result.Value!.Select(value => new BankConsentResponse(value.ConnectionId,
		 value.ProviderStatus, value.SessionExpiresAt, value.SessionExtendable)).ToArray());
	}

	private bool TryGetUserId(out Guid userId)
	{
		userId = Guid.Empty;
		if (User.Identity?.IsAuthenticated != true)
			return false;

		var id = currentUser.UserId;
		if (id is null || id == Guid.Empty)
			return false;

		userId = id.Value;
		return true;
	}

	private ObjectResult InvalidIdentity() => BankingProblem(
		StatusCodes.Status401Unauthorized, "BANKING_UNAUTHENTICATED", "Authentication is required.");

	private ObjectResult MapError(BankingErrorCode error, Guid? attemptId) => error switch
	{
		BankingErrorCode.NotFound => BankingProblem(
			StatusCodes.Status404NotFound, "BANKING_NOT_FOUND", "The banking resource was not found.", attemptId),
		BankingErrorCode.Conflict => BankingProblem(
			StatusCodes.Status409Conflict, "BANKING_CONFLICT", "The operation conflicts with the current state.", attemptId),
		BankingErrorCode.TemporarilyUnavailable => BankingProblem(
			StatusCodes.Status503ServiceUnavailable, "BANKING_TEMPORARILY_UNAVAILABLE",
			"The banking operation is temporarily unavailable.", attemptId),
		BankingErrorCode.Forbidden => BankingProblem(
			StatusCodes.Status403Forbidden, "BANKING_FORBIDDEN", "The user is inactive.", attemptId),
		_ => throw new ArgumentOutOfRangeException(nameof(error))
	};

	private ObjectResult BankingProblem(int statusCode, string code, string title, Guid? attemptId = null) => Problem(
		statusCode: statusCode,
		title: title,
		extensions: new Dictionary<string, object?> { ["code"] = code, ["attemptId"] = attemptId });

	private static BankConnectionResponse MapConnection(BankConnectionDetails value) => new(
		value.Id, value.ProviderName, value.ConnectionStatus, value.SyncStatus, value.CreatedAt,
		value.ConnectedAt, value.ConsentExpiresAt, value.LastSuccessfulSyncAt, value.LastSyncFailureCode);
}
