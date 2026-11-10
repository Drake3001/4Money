using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Expense.Services.Contracts;

/// <summary>A successful value or an expected business error, without HTTP semantics.</summary>
public sealed class ExpenseResult<T> where T : notnull
{
	public T? Value { get; }
	public IReadOnlyList<ValidationResult> ValidationErrors { get; } = [];
	public ExpenseErrorCode? Error { get; }
	public bool IsSuccess => Error is null;

	private ExpenseResult(T value) => Value = value;
	private ExpenseResult(ExpenseErrorCode error, IReadOnlyList<ValidationResult> errors)
	{
		Error = error;
		ValidationErrors = errors;
	}

	public static ExpenseResult<T> Success(T value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return new(value);
	}

	public static ExpenseResult<T> Failure(ExpenseErrorCode error, IReadOnlyList<ValidationResult>? errors = null)
	{
		if (!Enum.IsDefined(error))
			throw new ArgumentOutOfRangeException(nameof(error));
		return new(error, errors ?? []);
	}
}
