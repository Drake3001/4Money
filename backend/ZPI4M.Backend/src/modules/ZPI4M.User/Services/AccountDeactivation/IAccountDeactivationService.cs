using ZPI4M.User.DTOs.AccountDeactivation;

namespace ZPI4M.User.Services.AccountDeactivation;

public interface IAccountDeactivationService
{
	Task<RequestAccountDeactivationResult> RequestAsync(Guid userId, CancellationToken cancellationToken = default);
	Task<ConfirmAccountDeactivationResult> ConfirmAsync(ConfirmAccountDeactivationRequest request, CancellationToken cancellationToken = default);
}

public enum RequestAccountDeactivationResult
{
	Accepted,
	UserUnavailable,
	CooldownActive,
	EmailDeliveryFailed
}

public enum ConfirmAccountDeactivationResult
{
	Deactivated,
	InvalidOrExpiredToken
}
