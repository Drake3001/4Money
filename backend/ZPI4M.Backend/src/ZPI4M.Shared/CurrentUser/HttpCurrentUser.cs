using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace ZPI4M.Shared.CurrentUser;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
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
