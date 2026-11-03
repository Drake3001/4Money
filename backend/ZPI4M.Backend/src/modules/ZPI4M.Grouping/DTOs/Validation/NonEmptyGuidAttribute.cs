using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Validation;

public sealed class NonEmptyGuidAttribute : ValidationAttribute
{
	public override bool IsValid(object? value) => value is null || value is Guid id && id != Guid.Empty;
}
