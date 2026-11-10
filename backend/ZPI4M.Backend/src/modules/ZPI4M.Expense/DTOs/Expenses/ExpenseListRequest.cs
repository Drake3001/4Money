using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed class ExpenseListRequest : PagedQuery
{
	[Description("Without participantId, returns all accessible expenses in the group. If both groupId and participantId are omitted, returns expenses in which the caller has a share, including after leaving the group.")]
	public Guid? GroupId { get; set; }

	[Description("Filters accessible expenses by the user who shares the cost. This filter applies even without a group and does not grant access.")]
	public Guid? ParticipantId { get; set; }

	[Description("Filters by payer. Setting this field alone keeps the default filter for the caller's shares.")]
	public Guid? PayerId { get; set; }

	[Description("Inclusive start date.")]
	public DateOnly? TransactionTimeFrom { get; set; }

	[Description("Exclusive end date. from must be earlier than to.")]
	public DateOnly? TransactionTimeTo { get; set; }

	[Description("Inclusive minimum for the total expense amount.")]
	[RegularExpression(@"^[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string? MinAmount { get; set; }

	[Description("Inclusive maximum for the total expense amount. minAmount must be less than or equal to maxAmount.")]
	[RegularExpression(@"^[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string? MaxAmount { get; set; }

	[Description("Returns the full expense if any item matches the category. The system category Nieznane is allowed.")]
	public Guid? CategoryId { get; set; }

	[Description("Case-insensitive substring of the expense name or merchant. The filter is trimmed and blank values are ignored.")]
	public string? Search { get; set; }

	public bool? HasReceipt { get; set; }

	public bool? HasBankTransaction { get; set; }

	[Description("Values are case-insensitive, as in Banking. An unknown value returns 400.")]
	[Required]
	[RegularExpression(@"(?i)^(transactionTime|amount|name)$")]
	public string SortBy { get; set; } = "transactionTime";

	public ExpenseListRequest() => Descending = true;
}
