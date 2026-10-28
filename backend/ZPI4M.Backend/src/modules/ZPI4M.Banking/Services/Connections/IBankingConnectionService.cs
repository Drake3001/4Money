using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Services.Connections;

/// <summary>
/// Manages bank connections independently of HTTP and provider-specific contracts.
/// Every operation must be scoped to the supplied local 4Money user.
/// </summary>
public interface IBankingConnectionService
{
	/// <summary>
	/// Creates a connection attempt and its authorization URL using Continuous Access.
	/// Reuses the user's Permanent User mapping; must not replace existing bank connections.
	/// The attempt must be durably stored before returning a successful result.
	/// </summary>
	Task<BankingResult<StartBankConnectionResult>> StartConnectionAsync(
		Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns the attempt, including terminal states. Missing attempts and attempts owned
	/// by another user must both return NotFound.
	/// </summary>
	Task<BankingResult<BankConnectionAttemptDetails>> GetConnectionAttemptAsync(
		Guid userId, Guid attemptId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads local connections without requiring the provider to be available.
	/// Returns an empty collection when the user has no connections.
	/// </summary>
	Task<BankingResult<IReadOnlyList<BankConnectionDetails>>> GetConnectionsAsync(
		Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads a local connection without contacting the provider. Missing connections
	/// and connections owned by another user must both return NotFound.
	/// </summary>
	Task<BankingResult<BankConnectionDetails>> GetConnectionAsync(
		Guid userId, Guid connectionId, CancellationToken cancellationToken = default);
	/// <summary>
	/// Reads current provider consents for this user's locally mapped connections without persisting changes.
	/// Does not provision a Tink user or refresh banking data.
	/// </summary>
	Task<BankingResult<IReadOnlyList<BankConsentDetails>>> GetConsentsAsync(
		Guid userId, CancellationToken cancellationToken = default);
}
