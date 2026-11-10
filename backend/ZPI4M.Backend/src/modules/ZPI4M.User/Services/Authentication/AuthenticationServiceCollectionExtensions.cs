using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ZPI4M.User.Services.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
	public static IServiceCollection AddUserAuthentication(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<AuthenticationOptions>()
			.Bind(configuration.GetSection(AuthenticationOptions.SectionName))
			.ValidateDataAnnotations()
			.ValidateOnStart();
		services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
		services.AddScoped<IUserSessionService, UserSessionService>();
		services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();
		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
		services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
			.Configure<IOptions<AuthenticationOptions>>((bearer, configured) =>
			{
				var settings = configured.Value;
				bearer.MapInboundClaims = false;
				bearer.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidIssuer = settings.Issuer,
					ValidateAudience = true,
					ValidAudience = settings.Audience,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
					ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
					ClockSkew = TimeSpan.Zero,
					NameClaimType = "sub"
				};
				bearer.Events = new JwtBearerEvents
				{
					OnTokenValidated = async context =>
					{
						if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId) ||
							!Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sessionId) ||
							!await context.HttpContext.RequestServices.GetRequiredService<IUserAuthenticationService>()
								.IsSessionActiveAsync(userId, sessionId, context.HttpContext.RequestAborted))
						{
							context.Fail("The session is no longer active.");
						}
					}
				};
			});
		services.AddAuthorization();
		return services;
	}
}
