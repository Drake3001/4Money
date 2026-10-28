namespace ZPI4M.User.Services.Tokens;

public interface IUserTokenGenerator
{
	string GenerateToken();
	string HashToken(string token);
}
