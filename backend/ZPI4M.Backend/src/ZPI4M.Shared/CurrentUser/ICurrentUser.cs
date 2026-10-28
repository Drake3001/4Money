namespace ZPI4M.Shared.CurrentUser;

/// <summary>
/// Provides the local 4Money identity established by the application's authentication layer.
/// Implementations must use the validated principal, never request parameters or unverified headers.
/// </summary>
public interface ICurrentUser
{
	Guid? UserId { get; }
}
