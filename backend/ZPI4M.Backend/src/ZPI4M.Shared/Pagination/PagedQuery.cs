using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Shared.Pagination;

public class PagedQuery : IValidatableObject
{
	[Range(1, int.MaxValue)]
	public int PageNumber { get; set; } = 1;
	[Range(1, 100)]
	public int PageSize { get; set; } = 20;
	public bool Descending { get; set; }

	public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (((long)PageNumber - 1) * PageSize > int.MaxValue)
			yield return new("The requested page offset is too large.", [nameof(PageNumber)]);
	}

	public int GetOffset()
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(PageNumber, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(PageSize, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(PageSize, 100);
		var offset = ((long)PageNumber - 1) * PageSize;
		if (offset > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(PageNumber));
		return (int)offset;
	}
}
