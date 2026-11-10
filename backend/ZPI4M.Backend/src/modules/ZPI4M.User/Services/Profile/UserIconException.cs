namespace ZPI4M.User.Services.Profile;

public sealed class UserIconException(int status) : Exception
{
	public int Status { get; } = status;
}
