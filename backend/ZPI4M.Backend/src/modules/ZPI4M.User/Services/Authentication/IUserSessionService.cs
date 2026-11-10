using ZPI4M.User.DTOs.Authentication;
using ZPI4M.User.Models;

namespace ZPI4M.User.Services.Authentication;

public interface IUserSessionService
{
	Task<AuthenticationResponse> CreateAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);
	AuthenticationResponse CreateResponse(UserToken session, string rawRefreshToken, DateTime now);
}
