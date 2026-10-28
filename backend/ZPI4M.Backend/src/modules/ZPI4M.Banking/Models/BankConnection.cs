using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Models;

public sealed class BankConnection
{
	public Guid Id { get; set; }
	public Guid FourMoneyUserId { get; set; }
	public string TinkUserId { get; set; } = null!;
	public string CredentialsId { get; set; } = null!;
	public string? ProviderId { get; set; }
	public string? ProviderName { get; set; }
	public BankConnectionStatus ConnectionStatus { get; set; }
	public BankSynchronizationStatus SyncStatus { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset ConnectedAt { get; set; }
	public DateTimeOffset? ConsentExpiresAt { get; set; }
	public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
	public string? LastSyncFailureCode { get; set; }
}
