using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public partial class ExpenseItem
{
    public Guid Id { get; set; }

    public Guid FkExpenseId { get; set; }

    public Guid FkCategoryId { get; set; }

    public Guid? FkUserId { get; set; }

    public string Name { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public decimal Quantity { get; set; }

    public virtual Category FkCategory { get; set; } = null!;

    public virtual Expense FkExpense { get; set; } = null!;
}
