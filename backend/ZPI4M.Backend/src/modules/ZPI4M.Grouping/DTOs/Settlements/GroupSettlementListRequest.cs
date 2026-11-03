using System.ComponentModel.DataAnnotations;
using ZPI4M.Grouping.DTOs.Validation;
using ZPI4M.Grouping.Models;
using ZPI4M.Shared.Pagination;

namespace ZPI4M.Grouping.DTOs.Settlements;

public sealed class GroupSettlementListRequest : PagedQuery
{
	[NonEmptyGuid]
	public Guid? FromUserId { get; set; }
	[NonEmptyGuid]
	public Guid? ToUserId { get; set; }
	public DateTimeOffset? DateFrom { get; set; }
	public DateTimeOffset? DateTo { get; set; }
	[EnumDataType(typeof(SettlementMethod))]
	public SettlementMethod? Method { get; set; }

	public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		foreach (var error in base.Validate(validationContext)) yield return error;
		if (DateFrom > DateTo)
			yield return new("DateFrom must not exceed DateTo.", [nameof(DateFrom), nameof(DateTo)]);
	}
}
