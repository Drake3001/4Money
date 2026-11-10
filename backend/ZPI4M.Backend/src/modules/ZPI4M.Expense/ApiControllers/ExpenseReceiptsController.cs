using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.DTOs.Receipts;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Shared.CurrentUser;

namespace ZPI4M.Expense.ApiControllers;

[Route("api/expenses")]
public sealed class ExpenseReceiptsController(ICurrentUser currentUser, IExpenseService expenses) : ExpenseControllerBase(currentUser)
{
	[HttpPost("receipts/analyze", Name = "analyzeExpenseReceipt")]
	[EnableRateLimiting(ExpenseRegistration.ReceiptOcrRateLimitPolicy)]
	[EndpointSummary("Analyze a receipt without saving it")]
	[ProducesResponseType(typeof(ReceiptOcrResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
	[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
	[Consumes("multipart/form-data")]
	[RequestSizeLimit(ExpenseFormRequest.RequestLimit)]
	[RequestFormLimits(MultipartBodyLengthLimit = ExpenseFormRequest.RequestLimit)]
	public Task<IActionResult> AnalyzeExpenseReceipt([FromForm] ReceiptUploadRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => Request.Form.Files.Count == 1 ? expenses.AnalyzeReceiptAsync(actor, request.File, cancellationToken)
			: Task.FromResult(ExpenseResult<ReceiptOcrResponse>.Failure(ExpenseErrorCode.InvalidExpense,
				[new ValidationResult("Exactly one file is required.", ["File"])])), analysis => Ok(analysis));

	[HttpGet("{expenseId:guid}/receipt", Name = "getExpenseReceipt")]
	[EndpointSummary("Get the receipt for an expense")]
	[ProducesResponseType(typeof(ReceiptResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public Task<IActionResult> GetExpenseReceipt(Guid expenseId, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.GetReceiptAsync(actor, expenseId, cancellationToken), receipt => Ok(receipt));
}
