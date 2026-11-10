using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Expense.DTOs.Banking;

public sealed class AvailableBankTransactionRequest : PagedQuery
{
	[Description("The account must belong to the caller. A missing or inaccessible account returns 404.")]
	public Guid? BankAccountId { get; set; }

	[Description("Inclusive start date for bank transactions.")]
	public DateOnly? DateFrom { get; set; }

	[Description("Inclusive end date for bank transactions. from must be on or before to.")]
	public DateOnly? DateTo { get; set; }

	[Description("Inclusive lower bound for the original negative transaction amount.")]
	[RegularExpression(@"^-?[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string? MinAmount { get; set; }

	[Description("Inclusive upper bound for the original negative transaction amount. min must be less than or equal to max.")]
	[RegularExpression(@"^-?[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string? MaxAmount { get; set; }

	[Description("Case-insensitive substring of the transaction description. Blank values are ignored.")]
	public string? Description { get; set; }

	[Description("Values are case-insensitive, as in Banking. The endpoint always uses status=Booked and currencyCode=PLN.")]
	[Required]
	[RegularExpression(@"(?i)^(date|amount|status|currencyCode)$")]
	public string SortBy { get; set; } = "date";

	public AvailableBankTransactionRequest() => Descending = true;
}
