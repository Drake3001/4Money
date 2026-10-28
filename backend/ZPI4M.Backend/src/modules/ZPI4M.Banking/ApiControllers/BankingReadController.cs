using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.DTOs;
using ZPI4M.Banking.Services;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.ApiControllers;

/// <summary>Reads locally persisted accounts and transactions without contacting Tink or starting synchronization.</summary>
[ApiController]
[Authorize]
[Route("api/banking")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class BankingReadController(IBankingReadService service, ICurrentUser currentUser) : ControllerBase
{
	/// <summary>Lists the owner's local accounts, including closed and historical accounts unless filtered.</summary>
	[HttpGet("accounts")]
	[ProducesResponseType(typeof(PagedResult<BankAccountResponse>), StatusCodes.Status200OK)]
	public Task<IActionResult> GetAccountsAsync([FromQuery] BankAccountReadRequest request, CancellationToken ct) =>
		ReadAsync(user => service.GetAccountsAsync(user, request.ToQuery(), ct), page =>
			new PagedResult<BankAccountResponse>(page.Items.Select(MapAccount).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));

	/// <summary>Reads an owned local account. Foreign and missing accounts both return 404.</summary>
	[HttpGet("accounts/{accountId:guid}")]
	[ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetAccountAsync([NonEmptyGuid] Guid accountId, CancellationToken ct) =>
		ReadAsync(user => service.GetAccountAsync(user, accountId, ct), account => MapAccount(account));

	/// <summary>Lists local transactions. Dates/amount bounds are inclusive; no status filter includes all stored statuses.</summary>
	[HttpGet("transactions")]
	[ProducesResponseType(typeof(PagedResult<BankTransactionResponse>), StatusCodes.Status200OK)]
	public Task<IActionResult> GetTransactionsAsync([FromQuery] BankTransactionReadRequest request, CancellationToken ct) =>
		ReadAsync(user => service.GetTransactionsAsync(user, request.ToQuery(), ct), page =>
			new PagedResult<BankTransactionResponse>(page.Items.Select(MapTransaction).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));

	/// <summary>Reads an owned local transaction, preserving the signed amount as decimal text.</summary>
	[HttpGet("transactions/{transactionId:guid}")]
	[ProducesResponseType(typeof(BankTransactionResponse), StatusCodes.Status200OK)]
	public Task<IActionResult> GetTransactionAsync([NonEmptyGuid] Guid transactionId, CancellationToken ct) =>
		ReadAsync(user => service.GetTransactionAsync(user, transactionId, ct), transaction => MapTransaction(transaction));

	private async Task<IActionResult> ReadAsync<T>(Func<Guid, Task<BankingResult<T>>> read, Func<T, object> map) where T : notnull
	{
		if (User.Identity?.IsAuthenticated != true || currentUser.UserId is not Guid userId || userId == Guid.Empty)
			return Error(401, "BANKING_UNAUTHENTICATED");
		try
		{
			var result = await read(userId);
			if (result.IsSuccess) return Ok(map(result.Value!));
			return result.Error switch
			{
				BankingErrorCode.NotFound => Error(404, "BANKING_NOT_FOUND"),
				BankingErrorCode.Forbidden => Error(403, "BANKING_FORBIDDEN"),
				BankingErrorCode.TemporarilyUnavailable => Error(503, "BANKING_TEMPORARILY_UNAVAILABLE"),
				_ => throw new InvalidOperationException("Unexpected banking read result.")
			};
		}
		catch (Exception error) when (error is DbException or DbUpdateException)
		{
			return Error(503, "BANKING_TEMPORARILY_UNAVAILABLE");
		}
	}

	private ObjectResult Error(int status, string code) => Problem(statusCode: status,
		title: "The banking read operation could not be completed.", extensions: new Dictionary<string, object?> { ["code"] = code });

	private static BankAccountResponse MapAccount(BankAccountDetails account) => new(account.Id, account.Name,
		account.Number, account.Iban, account.FinancialInstitutionId, account.Type, account.CurrencyCode,
		account.IsClosed, account.LastSuccessfulSyncAt, account.TransactionsSyncedThrough);

	private static BankTransactionResponse MapTransaction(BankTransactionDetails transaction) => new(transaction.Id,
		transaction.BankAccountId, transaction.Amount.ToString("F2", CultureInfo.InvariantCulture), transaction.CurrencyCode,
		transaction.Date, transaction.Description, transaction.Status);
}
