using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ZPI4M.Expense.DTOs.Expenses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateExpenseShareRequest
{
	[JsonRequired]
	public Guid UserId { get; init; }

	[JsonRequired]
	[Required]
	public string Percentage { get; init; } = string.Empty;
}
