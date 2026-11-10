using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public partial class ExpenseItem
{
	public Guid Id { get; set; }

	public Guid FkExpenseId { get; set; }

	public Guid FkCategoryId { get; set; }

	public string Name { get; set; } = null!;

	public decimal UnitPrice { get; set; }

	public decimal Quantity { get; set; }

	public decimal Discount { get; set; }

	public string? Unit { get; set; }

	public bool IsSystemGenerated { get; set; }

	public virtual Category FkCategory { get; set; } = null!;

	public virtual Expense FkExpense { get; set; } = null!;
}
