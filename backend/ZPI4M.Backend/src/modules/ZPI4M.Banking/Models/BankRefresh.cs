namespace ZPI4M.Banking.Models;

public sealed class BankRefresh
{
	public Guid Id { get; set; }
	public Guid FourMoneyUserId { get; set; }
	public string TinkUserId { get; set; } = null!;
	public string Status { get; set; } = "QUEUED";
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public DateTimeOffset NextAttemptAt { get; set; }
	public Guid? LeaseId { get; set; }
	public DateTimeOffset? LeaseExpiresAt { get; set; }
	public int Attempts { get; set; }
	public bool DiscoveryCompleted { get; set; }
	public string? FailureCode { get; set; }
	public List<BankRefreshAccount> Accounts { get; set; } = [];
}

public sealed class BankRefreshAccount
{
	public Guid RefreshId { get; set; }
	public Guid AccountId { get; set; }
	public string TinkAccountId { get; set; } = null!;
	public string Status { get; set; } = "QUEUED";
	public string? FailureCode { get; set; }
	public bool Retryable { get; set; }
	public DateTimeOffset? RetryAfter { get; set; }
	public DateTimeOffset From { get; set; }
	public DateTimeOffset Through { get; set; }
	public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
	public int AddedTransactions { get; set; }
	public int UpdatedTransactions { get; set; }
}
