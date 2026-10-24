using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ZPI4M.User.Services.Tokens;

public sealed class UserTokenGenerator : IUserTokenGenerator
{
	public string GenerateToken()
	{
		return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
	}

	public string HashToken(string token)
	{
		ArgumentException.ThrowIfNullOrEmpty(token);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	}
}
