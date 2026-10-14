using System;
using System.Collections.Generic;

namespace ZPI4M.Banking.Models;

public partial class BankTransaction
{
    public Guid Id { get; set; }

    public Guid FkBankAccountId { get; set; }

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly Date { get; set; }

    public string CurrencyCode { get; set; } = null!;

    public string TinkTransactionId { get; set; } = null!;

    public virtual BankAccount FkBankAccount { get; set; } = null!;
}
