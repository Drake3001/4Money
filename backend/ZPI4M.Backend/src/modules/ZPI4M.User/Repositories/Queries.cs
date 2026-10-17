using ZPI4M.Shared.Pagination;

namespace ZPI4M.User.Repositories;

public enum UserSortBy { Login, Name, Surname, Email, BirthDate, CreatedAt, LastLoginTime, IsActive }

public class UserQuery : PagedQuery
{
	public string? Login { get; set; }
	public string? Name { get; set; }
	public string? Surname { get; set; }
	public string? Email { get; set; }
	public string? Phone { get; set; }
	public string? TinkUserId { get; set; }
	public bool? IsActive { get; set; }
	public UserSortBy SortBy { get; set; } = UserSortBy.Login;
}
