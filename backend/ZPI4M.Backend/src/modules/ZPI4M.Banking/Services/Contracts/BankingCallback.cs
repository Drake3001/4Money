namespace ZPI4M.Banking.Services.Contracts;

// Do not log this input: state is a one-time correlation secret.
public sealed class BankingCallback
{
	public string State { get; init; } = "";
	public string? CredentialsId { get; init; }
	public string? Error { get; init; }
	public string? ErrorReason { get; init; }
}
