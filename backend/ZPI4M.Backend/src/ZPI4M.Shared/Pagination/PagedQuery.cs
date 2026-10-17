namespace ZPI4M.Shared.Pagination;

public class PagedQuery
{
	public int PageNumber { get; set; } = 1;
	public int PageSize { get; set; } = 20;
	public bool Descending { get; set; }

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
