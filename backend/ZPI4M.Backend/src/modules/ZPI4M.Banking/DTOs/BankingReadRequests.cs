using System.ComponentModel.DataAnnotations;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Banking.DTOs;

public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
	public NonEmptyGuidAttribute() : base("A nonempty local ID is required.")
	{
	}

	public override bool IsValid(object? value) => value is null || value is Guid id && id != Guid.Empty;
}

public abstract class BankingPageRequest : PagedQuery
{
	protected static bool ValidSort<T>(string? value) where T : struct, Enum =>
		value is not null && Enum.GetNames<T>().Any(name =>
			string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class BankAccountReadRequest : BankingPageRequest
{
	public string? Name { get; set; }

	public string? CurrencyCode { get; set; }

	public string? Type { get; set; }

	public bool? IsClosed { get; set; }

	/// <summary>
	/// name (default), currencyCode or isClosed; names are case-insensitive.
	/// </summary>
	public string SortBy { get; set; } = "name";

	public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		foreach (var error in base.Validate(validationContext))
		{
			yield return error;
		}

		if (!ValidSort<BankAccountReadSortBy>(SortBy))
		{
			yield return new("SortBy must be name, currencyCode or isClosed.", [nameof(SortBy)]);
		}
	}

	public BankAccountReadQuery ToQuery() => new()
	{
		Name = Name,
		CurrencyCode = CurrencyCode,
		Type = Type,
		IsClosed = IsClosed,
		PageNumber = PageNumber,
		PageSize = PageSize,
		Descending = Descending,
		SortBy = Enum.Parse<BankAccountReadSortBy>(SortBy.Trim(), true)
	};
}

public sealed class BankTransactionReadRequest : BankingPageRequest
{
	/// <summary>Null ignores settlement assignments; true requires one; false excludes them.</summary>
	public bool? SettlementAssignment { get; set; }

	/// <summary>Null ignores expense assignments; true requires one; false excludes them.</summary>
	public bool? ExpenseAssignment { get; set; }

	[NonEmptyGuid]
	public Guid? BankAccountId { get; set; }

	public DateOnly? DateFrom { get; set; }

	public DateOnly? DateTo { get; set; }

	public decimal? MinAmount { get; set; }

	public decimal? MaxAmount { get; set; }

	public string? CurrencyCode { get; set; }

	public string? Description { get; set; }

	public string? Status { get; set; }

	/// <summary>
	/// date (default), amount, status or currencyCode; names are case-insensitive.
	/// </summary>
	public string SortBy { get; set; } = "date";

	public BankTransactionReadRequest()
	{
		Descending = true;
	}

	public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		foreach (var error in base.Validate(validationContext))
		{
			yield return error;
		}

		if (!ValidSort<BankTransactionReadSortBy>(SortBy))
		{
			yield return new("SortBy must be date, amount, status or currencyCode.", [nameof(SortBy)]);
		}

		if (DateFrom > DateTo)
		{
			yield return new("DateFrom must not exceed DateTo.", [nameof(DateFrom), nameof(DateTo)]);
		}

		if (MinAmount > MaxAmount)
		{
			yield return new("MinAmount must not exceed MaxAmount.", [nameof(MinAmount), nameof(MaxAmount)]);
		}
	}

	public BankTransactionReadQuery ToQuery() => new()
	{
		BankAccountId = BankAccountId,
		SettlementAssignment = SettlementAssignment,
		ExpenseAssignment = ExpenseAssignment,
		DateFrom = DateFrom,
		DateTo = DateTo,
		MinAmount = MinAmount,
		MaxAmount = MaxAmount,
		CurrencyCode = CurrencyCode,
		Description = Description,
		Status = Status,
		PageNumber = PageNumber,
		PageSize = PageSize,
		Descending = Descending,
		SortBy = Enum.Parse<BankTransactionReadSortBy>(SortBy.Trim(), true)
	};
}
