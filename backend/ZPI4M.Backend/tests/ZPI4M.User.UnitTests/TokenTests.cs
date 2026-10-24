using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using ZPI4M.User.Services.Authentication;
using ZPI4M.User.Services.Tokens;

namespace ZPI4M.User.UnitTests;

public sealed class TokenTests
{
	[Fact]
	public void UserTokensAreUniqueUrlSafeAndContain32RandomBytes()
	{
		var generator = new UserTokenGenerator();
		var tokens = Enumerable.Range(0, 100).Select(_ => generator.GenerateToken()).ToArray();
		Assert.Equal(tokens.Length, tokens.Distinct().Count());
		Assert.All(tokens, token =>
		{
			Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
			Assert.Equal(32, WebEncoders.Base64UrlDecode(token).Length);
		});
	}

	[Fact]
	public void TokenHashIsSha256AndCaseSensitive()
	{
		var generator = new UserTokenGenerator();
		Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", generator.HashToken("abc"));
		Assert.NotEqual(generator.HashToken("abc"), generator.HashToken("ABC"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void MissingTokenCannotBeHashed(string? token)
	{
		Assert.ThrowsAny<ArgumentException>(() => new UserTokenGenerator().HashToken(token!));
	}

	[Theory]
	[InlineData(60, 15)]
	[InlineData(5, 5)]
	public void AccessTokenIsSignedAndBoundToUserSessionAndSessionExpiry(int sessionMinutes, int expectedMinutes)
	{
		var settings = new AuthenticationOptions { SigningKey = "test-signing-key-at-least-32-bytes-long" };
		var generator = new AccessTokenGenerator(Options.Create(settings));
		var now = DateTime.UtcNow.AddSeconds(-1);
		var userId = Guid.NewGuid();
		var sessionId = Guid.NewGuid();
		var (token, expires) = generator.Generate(userId, sessionId, now, now.AddMinutes(sessionMinutes));
		var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
		var principal = handler.ValidateToken(token, new TokenValidationParameters
		{
			ValidIssuer = settings.Issuer,
			ValidAudience = settings.Audience,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
			ValidateIssuerSigningKey = true,
			ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
			ClockSkew = TimeSpan.Zero
		}, out var validated);
		Assert.Equal(now.AddMinutes(expectedMinutes), expires);
		Assert.Equal(userId.ToString(), principal.FindFirst("sub")?.Value);
		Assert.Equal(sessionId.ToString(), principal.FindFirst("sid")?.Value);
		Assert.True(Guid.TryParse(principal.FindFirst("jti")?.Value, out _));
		Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.IsType<JwtSecurityToken>(validated).Header.Alg);
		var other = generator.Generate(userId, sessionId, now, now.AddMinutes(sessionMinutes));
		Assert.NotEqual(token, other.Token);
	}
}
