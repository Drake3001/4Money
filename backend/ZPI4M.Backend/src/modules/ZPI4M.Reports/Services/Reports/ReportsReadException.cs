using System.ComponentModel.DataAnnotations;
using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Reports.Services.Reports;

/// <summary>Preserves an expected Expense read failure for the caller to handle.</summary>
public sealed class ReportsReadException(ExpenseErrorCode error, IReadOnlyList<ValidationResult> validationErrors)
	: Exception($"Expense report read failed: {error}.")
{
	public ExpenseErrorCode Error { get; } = error;
	public IReadOnlyList<ValidationResult> ValidationErrors { get; } = validationErrors;
}
