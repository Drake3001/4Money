using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public partial class Expense
{
    public Guid Id { get; set; }

    public Guid FkGroupId { get; set; }

    public Guid? FkBankTransactionId { get; set; }

    public Guid? FkReceiptId { get; set; }

    public string Name { get; set; } = null!;

    public string? Merchant { get; set; }

    public decimal Amount { get; set; }

    public DateTime TransactionTime { get; set; }

    public virtual ICollection<ExpenseItem> ExpenseItems { get; set; } = new List<ExpenseItem>();
}
