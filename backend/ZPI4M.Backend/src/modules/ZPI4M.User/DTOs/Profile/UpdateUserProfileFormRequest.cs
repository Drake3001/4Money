using Microsoft.AspNetCore.Http;

namespace ZPI4M.User.DTOs.Profile;

public sealed class UpdateUserProfileFormRequest : UpdateUserProfileRequest
{
	public IFormFile? IconFile { get; init; }
}
