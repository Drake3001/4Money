using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ZPI4M.Expense.DTOs.Expenses;

public sealed class ExpenseFormRequest
{
	public const int RequestLimit = 27 * 1024 * 1024;

	[Required]
	public string Expense { get; init; } = string.Empty;

	[Required]
	public IFormFile File { get; init; } = null!;

	[MaxLength(4 * 1024 * 1024)]
	public string? OcrProof { get; init; }
}
