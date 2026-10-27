namespace ZPI4M.Banking.Tink;

public interface ITinkDataGateway
{
	Task<ITinkDataSession> OpenAsync(string tinkUserId, CancellationToken cancellationToken);
}

public interface ITinkDataSession : IDisposable
{
	Task<IReadOnlyList<TinkBankAccount>> GetAccountsAsync(CancellationToken cancellationToken);
	Task<IReadOnlyList<TinkBankTransaction>> GetTransactionsAsync(string accountId, DateTimeOffset from,
		DateTimeOffset through, CancellationToken cancellationToken);
}

public sealed record TinkBankAccount(string Id, string Name, string? Number, string? Iban,
	string? FinancialInstitutionId, string? Type, string CurrencyCode);
public sealed record TinkBankTransaction(string Id, string AccountId, decimal Amount, string CurrencyCode,
	string Status, string? Description, DateOnly Date);
