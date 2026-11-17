using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ZPI4M.Expense.DTOs.Receipts;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Services.Contracts;
using ZPI4M.Receipt.Services;
using ReceiptRecord = ZPI4M.Receipt.Models.Receipt;

namespace ZPI4M.Expense.Services.Expenses;

public sealed partial class ExpenseService
{
	public async Task<ExpenseResult<ReceiptResponse>> GetReceiptAsync(Guid actor, Guid expenseId, CancellationToken cancellationToken)
	{
		if (expenseId == Guid.Empty) return ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.NotFound);
		var expense = await expenses.GetReadAsync(expenseId, cancellationToken);
		if (expense is null) return ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.NotFound);
		var accessError = await queries.CheckReadAccessAsync(actor, expense, cancellationToken);
		if (accessError.HasValue) return ExpenseResult<ReceiptResponse>.Failure(accessError.Value);
		if (expense.FkReceiptId is not Guid receiptId ||
			await receipts.GetByIdAsync(receiptId, cancellationToken) is not { } receipt)
			return ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.NotFound);

		var current = await expenses.GetReadAsync(expenseId, cancellationToken);
		if (current is null) return ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.NotFound);
		if (current.Version != expense.Version || current.FkReceiptId != receiptId)
			return ExpenseResult<ReceiptResponse>.Failure(ExpenseErrorCode.Conflict);

		var analysis = await MapAnalysisAsync(receipt.ExtractionResult, cancellationToken);
		var url = await receipts.GetFileUrlAsync(receipt, cancellationToken);
		return ExpenseResult<ReceiptResponse>.Success(new(receipt.Id, expenseId, url.Url, url.ExpiresAt.UtcDateTime, analysis));
	}

	public async Task<ExpenseResult<ReceiptOcrResponse>> AnalyzeReceiptAsync(Guid actor, IFormFile file, CancellationToken cancellationToken)
	{
		var validation = ValidateReceiptFile(file);
		if (validation.Value is not { } contentType) return Fail<ReceiptOcrResponse, string>(validation);
		try
		{
			var hash = await GetFileHashAsync(file, cancellationToken);
			await using var stream = file.OpenReadStream();
			var raw = await receipts.AnalyzeAsync(stream, contentType, cancellationToken);
			var analysis = await MapAnalysisAsync(raw.GetRawText(), cancellationToken);
			if (analysis is null) return ExpenseResult<ReceiptOcrResponse>.Failure(ExpenseErrorCode.UnprocessableReceipt);
			var proof = ocrProof.Create(actor, hash, raw);
			return proof is null ? ExpenseResult<ReceiptOcrResponse>.Failure(ExpenseErrorCode.TemporarilyUnavailable)
				: ExpenseResult<ReceiptOcrResponse>.Success(new(analysis.Proposal, analysis.Warnings, analysis.NeedsReview, proof));
		}
		catch (ReceiptAnalysisException error)
		{
			return ExpenseResult<ReceiptOcrResponse>.Failure(error.Kind switch
			{
				ReceiptAnalysisErrorKind.NoUsableData => ExpenseErrorCode.UnprocessableReceipt,
				ReceiptAnalysisErrorKind.HttpError when error.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.RequestEntityTooLarge
					=> ExpenseErrorCode.UnprocessableReceipt,
				ReceiptAnalysisErrorKind.Timeout => ExpenseErrorCode.OcrTimeout,
				_ => ExpenseErrorCode.OcrFailure
			});
		}
		catch (InvalidDataException)
		{
			return ExpenseResult<ReceiptOcrResponse>.Failure(ExpenseErrorCode.UnprocessableReceipt);
		}
	}

	private static ExpenseResult<string> ValidateReceiptFile(IFormFile file)
	{
		if (file.Length == 0) return Invalid<string>("File", "The file is empty.");
		if (file.Length > ReceiptService.MaximumFileBytes)
			return ExpenseResult<string>.Failure(ExpenseErrorCode.PayloadTooLarge);
		if (!MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType) ||
			mediaType.MediaType?.ToLowerInvariant() is not ("image/jpeg" or "image/png" or "image/webp"
				or "image/heic" or "image/heif" or "image/tiff" or "image/bmp" or "application/pdf"))
			return ExpenseResult<string>.Failure(ExpenseErrorCode.UnsupportedReceiptFormat);
		return ExpenseResult<string>.Success(mediaType.MediaType!);
	}

	private static async Task<string> GetFileHashAsync(IFormFile file, CancellationToken token)
	{
		await using var stream = file.OpenReadStream();
		return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
	}

	private async Task<ExpenseResult<ReceiptRecord>> UploadReceiptAsync(Guid actor, ReceiptAttachment attachment, CancellationToken token)
	{
		var validation = ValidateReceiptFile(attachment.File);
		if (validation.Value is not { } contentType) return Fail<ReceiptRecord, string>(validation);
		string? raw = null;
		if (attachment.OcrProof is { } proof)
		{
			raw = ocrProof.Verify(proof, actor, await GetFileHashAsync(attachment.File, token));
			if (raw is null) return Invalid<ReceiptRecord>("OcrProof", "Analyze this file again or save it without OCR.");
		}

		await using var stream = attachment.File.OpenReadStream();
		var receipt = await receipts.UploadForExpenseAsync(stream, contentType, raw, token);
		return ExpenseResult<ReceiptRecord>.Success(receipt);
	}

	private async Task RemoveReceiptAsync(Guid receiptId, CancellationToken token)
	{
		await transactions.EnlistAsync(receiptDb, token);
		var receipt = await receipts.GetByIdAsync(receiptId, token);
		if (receipt is null) return;
		await receipts.RemoveRecordAsync(receipt, token);
		transactions.AfterCommit(() => receipts.DeleteFileAsync(receipt));
	}

	private async Task<ReceiptAnalysisResponse?> MapAnalysisAsync(string? extractionResult, CancellationToken cancellationToken)
	{
		if (extractionResult is null) return null;
		using var document = JsonDocument.Parse(extractionResult);
		var raw = document.RootElement;
		var data = ReadProperty(raw, "receipt");
		if (data.ValueKind != JsonValueKind.Object) return null;

		var knownCategories = (await categories.GetAllAsync(cancellationToken))
			.Where(category => category.Id != ExpenseSystemCategories.UnknownId)
			.ToLookup(category => category.Name, StringComparer.OrdinalIgnoreCase);
		Guid? FindCategoryId(string? name)
		{
			var matches = name is null ? [] : knownCategories[name].Take(2).ToArray();
			return matches.Length == 1 ? matches[0].Id : null;
		}

		var depositCategoryId = FindCategoryId("kaucja");

		var draftItems = ReadArray(data, "items").Select(item =>
		{
			var categoryName = ReadText(item, "category");
			var categoryId = FindCategoryId(categoryName);
			var name = ReadText(item, "name_corrected") ?? ReadText(item, "name");
			var price = ReadDecimalText(item, "unit_price", 2);
			var quantity = ReadDecimalText(item, "quantity", 3, value => value > 0m);
			var discount = ReadDecimalText(item, "discount", 2, value => value <= 0m);
			if (string.Equals(categoryName, "zniżka", StringComparison.OrdinalIgnoreCase) &&
				price is null && quantity is null &&
				(!ExpenseRequestService.TryParseDecimal(discount, 2, out var productDiscount) || productDiscount == 0m) &&
				ReadDecimalText(item, "total", 2, value => value < 0m) is { } discountTotal)
			{
				price = discountTotal;
				quantity = "1";
				discount = "0.00";
			}

			return new ReceiptDraftItem(
				CategoryId: categoryId,
				Name: name,
				UnitPrice: price,
				Quantity: quantity,
				Discount: discount,
				Unit: ReadText(item, "unit"),
				NeedsReview: ReadProperty(item, "needs_review").ValueKind == JsonValueKind.True,
				ReviewReason: ReadText(item, "review_reason"));
		}).Concat(ReadArray(data, "deposits").Select(item => MapReceiptAdjustment(item, depositCategoryId, isReturn: false)))
			.Concat(ReadArray(data, "deposit_returns").Select(item => MapReceiptAdjustment(item, depositCategoryId, isReturn: true)))
			.ToArray();

		var store = ReadText(data, "store");
		DateOnly? transactionTime = DateOnly.TryParseExact(ReadText(data, "date"), "yyyy-MM-dd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
		var amount = ReadDecimalText(data, "amount_due", 2, value => value > 0m)
			?? ReadDecimalText(data, "total", 2, value => value > 0m);
		var proposal = new ReceiptDraft(
			Name: store,
			Merchant: store,
			Amount: amount,
			CurrencyCode: ReadText(data, "currency")?.ToUpperInvariant(),
			TransactionTime: transactionTime,
			Items: draftItems);
		var warnings = ReadArray(raw, "warnings")
			.Select(warning =>
			{
				var message = warning.ValueKind == JsonValueKind.String
					? warning.GetString()
					: ReadText(warning, "message");
				return message is null
					? null
					: new ReceiptWarning(ReadText(warning, "code"), message, ReadText(warning, "field"));
			})
			.OfType<ReceiptWarning>()
			.ToArray();
		var needsReview = ReadProperty(raw, "needs_review").ValueKind == JsonValueKind.True || draftItems.Any(item => item.NeedsReview);
		return new ReceiptAnalysisResponse(proposal, warnings, needsReview, raw.Clone());
	}

	private static ReceiptDraftItem MapReceiptAdjustment(JsonElement item, Guid? categoryId, bool isReturn)
	{
		bool HasExpectedSign(decimal value) => isReturn ? value < 0m : value > 0m;

		var name = ReadText(item, "name") ?? (isReturn ? "Zwrot kaucji" : "Kaucja lub opłata");
		var reportedTotalText = ReadDecimalText(item, "total", 2);
		var hasReportedTotal = ExpenseRequestService.TryParseDecimal(reportedTotalText, 2, out var reportedTotal);
		var total = hasReportedTotal && HasExpectedSign(reportedTotal) ? reportedTotalText : null;
		var price = ReadDecimalText(item, "unit_price", 2, HasExpectedSign);
		var quantity = ReadDecimalText(item, "quantity", 3, value => value > 0m);

		if (ExpenseRequestService.TryParseDecimal(price, 2, out var unitPrice) &&
			ExpenseRequestService.TryParseDecimal(quantity, 3, out var count))
		{
			if (ExpenseRequestService.TryGetTotal(unitPrice, count, 0m, out var computed) &&
				HasExpectedSign(computed) && (!hasReportedTotal || computed == reportedTotal))
				return new(categoryId, name, price, quantity, "0.00", null,
					categoryId is null, categoryId is null ? "missing_category" : null);

			return new(categoryId, name, total, total is null ? null : "1", "0.00", null, true, "amount_mismatch");
		}

		return new(categoryId, name, total, total is null ? null : "1", "0.00", null,
			total is null || categoryId is null,
			total is null ? "missing_amount" : categoryId is null ? "missing_category" : null);
	}

	private static JsonElement ReadProperty(JsonElement element, string name)
	{
		return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
			? value : default;
	}

	private static IEnumerable<JsonElement> ReadArray(JsonElement element, string name)
	{
		var value = ReadProperty(element, name);
		return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
	}

	private static string? ReadText(JsonElement element, string name)
	{
		var value = ReadProperty(element, name);
		var text = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
		return string.IsNullOrEmpty(text) ? null : text;
	}

	private static string? ReadDecimalText(JsonElement element, string name, int scale, Func<decimal, bool>? accept = null)
	{
		var value = ReadProperty(element, name);
		var text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : ReadText(element, name);
		if (!ExpenseRequestService.TryParseDecimal(text, scale, out var number) || (accept is not null && !accept(number)))
			return null;
		return text;
	}
}
