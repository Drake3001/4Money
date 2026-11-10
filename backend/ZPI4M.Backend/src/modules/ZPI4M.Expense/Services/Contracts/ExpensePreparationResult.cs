using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpensePreparationResult(
	PreparedExpense? Value,
	IReadOnlyList<ValidationResult> Errors);
