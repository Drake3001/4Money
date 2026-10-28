namespace ZPI4M.Banking.Models;

public partial class BankAccount
{
	public Guid Id { get; set; }

	public Guid FkUserId { get; set; }

	public string Name { get; set; } = null!;

	public string? Number { get; set; }

	public string? FinancialInstitutionId { get; set; }

	public string? Type { get; set; }

	public string CurrencyCode { get; set; } = null!;

	public string? Iban { get; set; }

	public string? CredentialsId { get; set; }

	public string? TinkAccountId { get; set; }

	public string? TinkUserId { get; set; }

	public DateTimeOffset? LastSuccessfulSyncAt { get; set; }

	public DateTimeOffset? TransactionsSyncedThrough { get; set; }

	public bool IsClosed { get; set; }

	public virtual ICollection<BankTransaction> BankTransactions { get; set; } = new List<BankTransaction>();
}
