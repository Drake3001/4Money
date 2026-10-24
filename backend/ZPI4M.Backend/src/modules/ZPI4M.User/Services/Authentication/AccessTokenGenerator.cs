using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ZPI4M.User.Services.Authentication;

public sealed class AccessTokenGenerator(IOptions<AuthenticationOptions> options) : IAccessTokenGenerator
{
	public (string Token, DateTime ExpiresAt) Generate(Guid userId, Guid sessionId, DateTime utcNow, DateTime sessionExpiresAt)
	{
		var settings = options.Value;
		var expiresAt = utcNow.AddMinutes(settings.AccessTokenLifetimeMinutes);
		if (expiresAt > sessionExpiresAt)
		{
			expiresAt = sessionExpiresAt;
		}
		var token = new JwtSecurityToken(
			issuer: settings.Issuer,
			audience: settings.Audience,
			claims:
			[
				new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
				new Claim("sid", sessionId.ToString()),
				new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
			],
			notBefore: utcNow,
			expires: expiresAt,
			signingCredentials: new SigningCredentials(
				new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256));
		return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
	}
}
