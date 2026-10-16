using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public partial class Category
{
	public Guid Id { get; set; }

	public string Name { get; set; } = null!;

	public string Description { get; set; } = null!;

	public string Icon { get; set; } = null!;

	public string Color { get; set; } = null!;

	public virtual ICollection<ExpenseItem> ExpenseItems { get; set; } = new List<ExpenseItem>();
}
