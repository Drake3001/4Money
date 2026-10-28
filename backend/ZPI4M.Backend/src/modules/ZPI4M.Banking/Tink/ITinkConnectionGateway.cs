namespace ZPI4M.Banking.Tink;

public interface ITinkConnectionGateway
{
	Task<string> CreateOrRecoverUserAsync(Guid userId, CancellationToken cancellationToken);
	Task<string> CreateLinkAsync(string tinkUserId, string idHint, string state, CancellationToken cancellationToken);
	Task<TinkVerifiedConnection?> VerifyConnectionAsync(string tinkUserId, string credentialsId, CancellationToken cancellationToken);
	Task<IReadOnlyList<TinkProviderConsent>> GetConsentsAsync(string tinkUserId, CancellationToken cancellationToken);
}

public sealed record TinkVerifiedConnection(string CredentialsId, string ProviderId);

public sealed record TinkProviderConsent(string CredentialsId, string ProviderStatus,
 DateTimeOffset? SessionExpiresAt, bool? SessionExtendable);
