namespace ZPI4M.Banking.Services.Contracts;

/// <summary>A successful value or an expected application error, without HTTP semantics.</summary>
public sealed class BankingResult<T> where T : notnull
{
	public T? Value { get; }
	public BankingErrorCode? Error { get; }
	public Guid? AttemptId { get; }
	public bool IsSuccess => Error is null;

	private BankingResult(T value) => Value = value;
	private BankingResult(BankingErrorCode error, Guid? attemptId)
	{
		Error = error;
		AttemptId = attemptId;
	}

	public static BankingResult<T> Success(T value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return new(value);
	}

	public static BankingResult<T> Failure(BankingErrorCode error, Guid? attemptId = null)
	{
		if (!Enum.IsDefined(error))
			throw new ArgumentOutOfRangeException(nameof(error));
		return new(error, attemptId);
	}
}
