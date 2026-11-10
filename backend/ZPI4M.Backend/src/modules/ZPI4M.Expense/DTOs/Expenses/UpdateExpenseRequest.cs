using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ZPI4M.Expense.DTOs.Receipts;

namespace ZPI4M.Expense.DTOs.Expenses;

/// <summary>Complete replacement form for an existing expense.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateExpenseRequest
{
	[JsonRequired]
	[Required]
	public string Version { get; init; } = string.Empty;

	[JsonRequired]
	public Guid PayerId { get; init; }

	[Description("Required. Must contain text after trimming.")]
	[JsonRequired]
	[Required]
	public string Name { get; init; } = string.Empty;

	[Description("Text is trimmed. Omit, pass null, or pass whitespace to clear the merchant.")]
	public string? Merchant { get; init; }

	[Description("Omit or pass null to recalculate the amount from the items, including on PUT. A supplied amount is the target total, with any difference added as Nieznane. When linking a bank transaction, supply the absolute amount of the debit.")]
	[RegularExpression(@"^[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string? Amount { get; init; }

	[Description("Purchase date in YYYY-MM-DD format. Future dates are allowed.")]
	[JsonRequired]
	public DateOnly TransactionTime { get; init; }

	[Description("Send regular items only. An empty list is allowed when a positive amount is supplied. On PUT, this list replaces all existing items and omitted items are removed.")]
	[JsonRequired]
	[Required]
	public IReadOnlyList<CreateExpenseItemRequest> Items { get; init; } = [];

	[Description("Provide the full percentage split. Omit zero shares.")]
	[JsonRequired]
	[Required]
	[MinLength(1)]
	public IReadOnlyList<CreateExpenseShareRequest> Shares { get; init; } = [];

	[Description("On POST, omit or pass null to create an expense without a bank transaction. On PUT, pass null to unlink the transaction or provide an ID to keep or replace it. A new transaction must belong to the caller. Another authorized group member may keep an existing link.")]
	[JsonRequired]
	public Guid? BankTransactionId { get; init; }

	[Description("Remove the currently attached receipt.")]
	public bool RemoveReceipt { get; init; }

	internal ReceiptAttachment? Attachment { get; set; }

}
