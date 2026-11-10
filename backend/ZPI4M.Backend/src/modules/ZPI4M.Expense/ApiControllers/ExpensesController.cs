using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ZPI4M.Expense.DTOs.Banking;
using ZPI4M.Expense.DTOs.Expenses;
using ZPI4M.Expense.DTOs.Receipts;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.ApiControllers;

[Route("api/expenses")]
public sealed class ExpensesController(ICurrentUser currentUser, IExpenseService expenses) : ExpenseControllerBase(currentUser)
{
	[HttpGet(Name = "listExpenses")]
	[EndpointSummary("List expenses")]
	[EndpointDescription("Filters and access checks apply before pagination and totalCount. The response uses locally stored data. It includes profile and group names and omits email addresses and bank account details.")]
	[ProducesResponseType(typeof(PagedResult<ExpenseListItemResponse>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public Task<IActionResult> ListExpenses([FromQuery] ExpenseListRequest request, CancellationToken cancellationToken)
	{
		foreach (var field in new[] { "transactionTimeFrom", "transactionTimeTo" })
		{
			if (Request.Query.TryGetValue(field, out var values) && (values.Count != 1 || !DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
				ModelState.AddModelError(field, "Use a date in YYYY-MM-DD format.");
		}
		if (!ModelState.IsValid) return Task.FromResult<IActionResult>(ValidationProblem(ModelState));

		return ExecuteAsync(actor => expenses.ListAsync(actor, request, cancellationToken), page =>
			Ok(new PagedResult<ExpenseListItemResponse>(page.Items.Select(ExpenseResponseMapper.HistoryEntry).ToArray(),
				page.TotalCount, page.PageNumber, page.PageSize)));
	}

	[HttpGet("{expenseId:guid}", Name = "getExpense")]
	[EndpointSummary("Get expense details")]
	[EndpointDescription("Current group members can read all group expenses. Former members can read an expense if they paid it or have a share in it. For former members, canEdit and canDelete are false. An inaccessible expense returns 404.")]
	[ProducesResponseType(typeof(ExpenseResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public Task<IActionResult> GetExpense(Guid expenseId, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.GetByIdAsync(actor, expenseId, cancellationToken), view => Ok(ExpenseResponseMapper.Details(view)));

	[HttpPost("preview", Name = "previewExpense")]
	[EndpointSummary("Preview an expense")]
	[EndpointDescription("Calculates the expense without saving it or running OCR. Previewing an edit requires the current version.")]
	[ProducesResponseType(typeof(ExpensePreviewResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
	[Consumes("application/json")]
	public Task<IActionResult> PreviewExpense([FromBody] PreviewExpenseRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.PreviewAsync(actor, request, cancellationToken), view => Ok(ExpenseResponseMapper.Preview(view)));

	[HttpPost(Name = "createExpense")]
	[EndpointSummary("Create an expense")]
	[EndpointDescription("Saves the expense, regular and system items, percentage shares, and optional source links in one transaction. The endpoint does not support idempotency keys. If the response is lost, check the expense history before retrying.")]
	[ProducesResponseType(typeof(ExpenseResponse), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
	[Consumes("application/json")]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
	[RequestSizeLimit(ExpenseFormRequest.RequestLimit)]
	[RequestFormLimits(MultipartBodyLengthLimit = ExpenseFormRequest.RequestLimit, ValueLengthLimit = ReceiptOcrProof.MaximumLength)]
	public Task<IActionResult> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.CreateAsync(actor, request, cancellationToken), view =>
			CreatedAtAction(nameof(GetExpense), new { expenseId = view.Expense.Id }, ExpenseResponseMapper.Details(view)));

	[HttpPost, Consumes("multipart/form-data")]
	// The host OpenAPI transformer adds multipart to the corresponding JSON operation.
	[ApiExplorerSettings(IgnoreApi = true)]
	[RequestSizeLimit(ExpenseFormRequest.RequestLimit)]
	[RequestFormLimits(MultipartBodyLengthLimit = ExpenseFormRequest.RequestLimit, ValueLengthLimit = ReceiptOcrProof.MaximumLength)]
	public Task<IActionResult> CreateForm([FromForm] ExpenseFormRequest form,
		[FromServices] IOptions<JsonOptions> jsonOptions, CancellationToken cancellationToken)
	{
		if (!TryReadForm<CreateExpenseRequest>(form, jsonOptions.Value.JsonSerializerOptions, out var request))
			return Task.FromResult<IActionResult>(ValidationProblem(ModelState));
		request!.Attachment = new ReceiptAttachment(form.File, form.OcrProof);
		return CreateExpense(request, cancellationToken);
	}

	[HttpPut("{expenseId:guid}", Name = "updateExpense")]
	[EndpointSummary("Replace an expense")]
	[EndpointDescription("Saves the complete edit form and requires the current version. A new file replaces the attached receipt. Set removeReceipt to remove it.")]
	[ProducesResponseType(typeof(ExpenseResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
	[Consumes("application/json")]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
	[RequestSizeLimit(ExpenseFormRequest.RequestLimit)]
	[RequestFormLimits(MultipartBodyLengthLimit = ExpenseFormRequest.RequestLimit, ValueLengthLimit = ReceiptOcrProof.MaximumLength)]
	public Task<IActionResult> UpdateExpense(Guid expenseId, [FromBody] UpdateExpenseRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.UpdateAsync(actor, expenseId, request, cancellationToken), view => Ok(ExpenseResponseMapper.Details(view)));

	[HttpPut("{expenseId:guid}"), Consumes("multipart/form-data")]
	[ApiExplorerSettings(IgnoreApi = true)]
	[RequestSizeLimit(ExpenseFormRequest.RequestLimit)]
	[RequestFormLimits(MultipartBodyLengthLimit = ExpenseFormRequest.RequestLimit, ValueLengthLimit = ReceiptOcrProof.MaximumLength)]
	public Task<IActionResult> UpdateForm(Guid expenseId, [FromForm] ExpenseFormRequest form,
		[FromServices] IOptions<JsonOptions> jsonOptions, CancellationToken cancellationToken)
	{
		if (!TryReadForm<UpdateExpenseRequest>(form, jsonOptions.Value.JsonSerializerOptions, out var request))
			return Task.FromResult<IActionResult>(ValidationProblem(ModelState));
		request!.Attachment = new ReceiptAttachment(form.File, form.OcrProof);
		return UpdateExpense(expenseId, request, cancellationToken);
	}

	private bool TryReadForm<T>(ExpenseFormRequest form, JsonSerializerOptions options, out T? request) where T : class
	{
		request = null;
		if (Request.Form.Files.Count != 1 || Request.Form.Files[0].Name != "file" ||
			Request.Form.Keys.Any(key => key is not ("expense" or "ocrProof")) ||
			Request.Form["expense"].Count != 1 || Request.Form["ocrProof"].Count > 1)
		{
			ModelState.AddModelError("file", "Submit expense JSON, one file and optionally one ocrProof.");
			return false;
		}
		try { request = JsonSerializer.Deserialize<T>(form.Expense, options); }
		catch (JsonException) { ModelState.AddModelError("expense", "Invalid expense JSON."); }
		if (request is null)
		{
			if (ModelState.IsValid) ModelState.AddModelError("expense", "Expense JSON is required.");
			return false;
		}
		return TryValidateModel(request);
	}

	[HttpDelete("{expenseId:guid}", Name = "deleteExpense")]
	[EndpointSummary("Delete an expense")]
	[EndpointDescription("Requires current group membership and the current version. Deletes the expense, its items, shares, and receipt record. The linked bank transaction becomes available again. A missing expense returns 404. The receipt file is deleted after the database commit. A storage failure does not restore the expense.")]
	[ProducesResponseType(StatusCodes.Status204NoContent)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
	public Task<IActionResult> DeleteExpense(Guid expenseId, [FromQuery, Required] string version, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.DeleteAsync(actor, expenseId, version, cancellationToken), _ => NoContent());

	[HttpGet("available-bank-transactions", Name = "listAvailableExpenseBankTransactions")]
	[EndpointSummary("List bank transactions available for linking")]
	[EndpointDescription("Returns the caller's unassigned, booked PLN debits. Assigned transactions are excluded before pagination and totalCount. The currently linked transaction ID appears in the expense details. Listing transactions does not reserve them.")]
	[ProducesResponseType(typeof(PagedResult<AvailableBankTransactionResponse>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
	public Task<IActionResult> ListAvailableExpenseBankTransactions([FromQuery] AvailableBankTransactionRequest request, CancellationToken cancellationToken) =>
		ExecuteAsync(actor => expenses.GetAvailableBankTransactionsAsync(actor, request, cancellationToken), page =>
			Ok(new PagedResult<AvailableBankTransactionResponse>(page.Items.Select(ExpenseResponseMapper.AvailableBankTransaction).ToArray(),
				page.TotalCount, page.PageNumber, page.PageSize)));
}
