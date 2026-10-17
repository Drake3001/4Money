namespace ZPI4M.Shared.Pagination;

public class PagedResult<T>
{
	public IReadOnlyList<T> Items { get; set; }
	public int TotalCount { get; set; }
	public int PageNumber { get; set; }
	public int PageSize { get; set; }
	public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
	public bool HasPreviousPage => PageNumber > 1;
	public bool HasNextPage => PageNumber < TotalPages;

	public PagedResult(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
	{
		Items = items;
		TotalCount = totalCount;
		PageNumber = pageNumber;
		PageSize = pageSize;
	}
}
