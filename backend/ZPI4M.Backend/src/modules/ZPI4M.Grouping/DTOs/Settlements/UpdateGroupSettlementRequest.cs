using System.ComponentModel.DataAnnotations;
using ZPI4M.Grouping.DTOs.Validation;
using ZPI4M.Grouping.Models;

namespace ZPI4M.Grouping.DTOs.Settlements;

public sealed class UpdateGroupSettlementRequest : IValidatableObject
{
	[Required, NonEmptyGuid]
	public Guid? FromUserId { get; init; }
	[Required, NonEmptyGuid]
	public Guid? ToUserId { get; init; }
	[Required, SettlementAmount]
	public decimal? Amount { get; init; }
	[Required, EnumDataType(typeof(SettlementMethod))]
	public SettlementMethod? Method { get; init; }
	[Required]
	public DateTimeOffset? SettledAt { get; init; }
	[NonEmptyGuid]
	public Guid? BankTransactionId { get; init; }

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (FromUserId.HasValue && FromUserId == ToUserId)
			yield return new("Settlement parties must differ.", [nameof(FromUserId), nameof(ToUserId)]);
	}
}
