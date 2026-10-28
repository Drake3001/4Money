namespace ZPI4M.Banking.Services.Connections;

public sealed class BankingConnectionOptions
{
	public int AttemptLifetimeMinutes { get; set; } = 15;
}
