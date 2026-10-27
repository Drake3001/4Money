using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Models;

public sealed class BankConnectionAttempt
{
	public Guid Id { get; set; }
	public Guid FourMoneyUserId { get; set; }
	public string? TinkUserId { get; set; }
	public BankConnectionAttemptStatus Status { get; set; }
	public string CorrelationStateHash { get; set; } = null!;
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset ExpiresAt { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
	public Guid? BankConnectionId { get; set; }
	public string? FailureCode { get; set; }
	public long Version { get; set; }
}
