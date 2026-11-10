using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateExpenseItemRequest
{
	[Description("Omit or pass null for a new item. When editing, supply an existing regular item ID from this expense to retain its identity. Each ID may appear once.")]
	public Guid? Id { get; init; }

	[Description("ID of an existing non-system category.")]
	[JsonRequired]
	public Guid CategoryId { get; init; }

	[Description("Required. Must contain text after trimming.")]
	[JsonRequired]
	[Required]
	public string Name { get; init; } = string.Empty;

	[JsonRequired]
	[Required]
	public string UnitPrice { get; init; } = string.Empty;

	[JsonRequired]
	[Required]
	public string Quantity { get; init; } = string.Empty;

	[Description("Discount for the whole item. Defaults to 0.00. It must be zero or negative, and its magnitude cannot exceed the rounded value of a non-negative item. If unitPrice is negative, the discount must be zero.")]
	[RegularExpression(@"^-?[0-9]{1,16}(\.[0-9]{1,2})?$")]
	public string Discount { get; init; } = "0.00";

	[Description("Text is trimmed. Omit, pass null, or pass whitespace to leave the unit unset.")]
	public string? Unit { get; init; }
}
