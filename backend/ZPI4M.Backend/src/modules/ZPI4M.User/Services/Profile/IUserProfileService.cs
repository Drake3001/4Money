using ZPI4M.User.DTOs.Profile;

namespace ZPI4M.User.Services.Profile;

public interface IUserProfileService
{
	Task<UserProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<(UpdateUserProfileResult Result, UserProfileResponse? Profile)> UpdateAsync(
		Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
}

public enum UpdateUserProfileResult
{
	Updated,
	UserUnavailable,
	LoginAlreadyInUse,
	InvalidRequest
}
