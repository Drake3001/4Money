using System.ComponentModel.DataAnnotations;
using ZPI4M.Grouping.DTOs.Validation;

namespace ZPI4M.Grouping.DTOs.Groups;

public sealed class TransferGroupAdministratorRequest
{
	[Required, NonEmptyGuid]
	public Guid? UserId { get; init; }
}
