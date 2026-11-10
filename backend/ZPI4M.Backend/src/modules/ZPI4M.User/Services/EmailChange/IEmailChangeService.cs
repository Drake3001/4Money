using ZPI4M.User.DTOs.EmailChange;

namespace ZPI4M.User.Services.EmailChange;

public interface IEmailChangeService
{
	Task<ChangeEmailResult> RequestAsync(Guid userId, ChangeEmailRequest request, CancellationToken cancellationToken = default);
	Task<ConfirmEmailChangeResult> ConfirmAsync(ConfirmEmailChangeRequest request, CancellationToken cancellationToken = default);
}

public enum ChangeEmailResult
{
	Accepted,
	UserUnavailable,
	InvalidPassword,
	EmailUnchanged,
	EmailAlreadyInUse,
	CooldownActive,
	EmailDeliveryFailed,
	GoogleAccountEmailChangeForbidden
}

public enum ConfirmEmailChangeResult
{
	Confirmed,
	InvalidOrExpiredToken,
	EmailAlreadyInUse
}
