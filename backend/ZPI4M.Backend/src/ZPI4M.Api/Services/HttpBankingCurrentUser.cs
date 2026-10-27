using System.Security.Claims;
using ZPI4M.Banking.Services;

namespace ZPI4M.Api.Services;

public sealed class HttpBankingCurrentUser(IHttpContextAccessor accessor) : IBankingCurrentUser
{
	public Guid? UserId
	{
		get
		{
			var identity = accessor.HttpContext?.User.Identities.FirstOrDefault(x => x.IsAuthenticated);
			var userId = identity?.FindFirst("sub")?.Value ?? identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
			return Guid.TryParse(userId, out var id) && id != Guid.Empty ? id : null;
		}
	}
}
