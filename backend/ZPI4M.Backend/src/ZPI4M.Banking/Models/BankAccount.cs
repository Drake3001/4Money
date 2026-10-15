using System;
using System.Collections.Generic;

namespace ZPI4M.Banking.Models;

public partial class BankAccount
{
	public Guid Id { get; set; }

	public Guid FkUserId { get; set; }

	public string Name { get; set; } = null!;

	public string Number { get; set; } = null!;

	public string FinancialInstitutionId { get; set; } = null!;

	public string? Type { get; set; }

	public string CurrencyCode { get; set; } = null!;

	public string Iban { get; set; } = null!;

	public string? CredentialsId { get; set; }

	public bool IsClosed { get; set; }

	public virtual ICollection<BankTransaction> BankTransactions { get; set; } = new List<BankTransaction>();
}
